#include "ddoffscreenrender.h"

#include <tchar.h>
#include <time.h>
#include <stdio.h>
#include <vector>
extern std::vector<HWNDHMONITOR> g_GUID_List;

#define SAFE_DELETE(p)       { if(p) { delete (p);     (p)=NULL; } }
#define SAFE_DELETE_ARRAY(p) { if(p) { delete[] (p);   (p)=NULL; } }
#define SAFE_RELEASE(p)      { if(p) { (p)->Release(); (p)=NULL; } }

typedef int (CALLBACK *FARPROC2)();
HMONITOR (WINAPI* g_pfnMonitorFromWindow2)(HWND, DWORD) = NULL;
HMONITOR (WINAPI* g_pfnMonitorFromPoint2)(POINT, DWORD) = NULL;
int      (WINAPI* g_pfnGetSystemMetrics2)(int) = NULL;

DDPIXELFORMAT ddpfPixelFormats[] = 
{
	{sizeof(DDPIXELFORMAT), DDPF_FOURCC,MAKEFOURCC('U','Y','V','Y'),0,0,0,0,0}, // UYVY
	{sizeof(DDPIXELFORMAT), DDPF_FOURCC,MAKEFOURCC('Y','U','Y','2'),0,0,0,0,0},  // YUY2
	{sizeof(DDPIXELFORMAT), DDPF_FOURCC,MAKEFOURCC('Y','V','1','2'),0,0,0,0,0},  // YV12	
	{sizeof(DDPIXELFORMAT), DDPF_FOURCC,MAKEFOURCC('Y','V','U','9'),0,0,0,0,0},  // YVU9
	{sizeof(DDPIXELFORMAT), DDPF_FOURCC,MAKEFOURCC('I','F','0','9'),0,0,0,0,0},  // IF09
	{sizeof(DDPIXELFORMAT), DDPF_RGB, 0, 32, 0x00FF0000,0x0000FF00,0x000000FF, 0} //RGB32
};

color_convert_func ccfunc[] = 
{
	yuv2uyvy16_mmx,
	yuv2yuyv16_mmx,	
	yuv2yv12,	
	0
};

DDOffscreenRender::DDOffscreenRender()
: m_pDD(0), m_pDDSPrimary(0), m_pDDSVideoSurface(0), m_pDDRGBSurface(0), m_bVerticalSyncEnable(FALSE)
{
	m_index		= -1;
	m_hWnd		= 0;
	m_width		= 352;
	m_height	= 288;
	m_callback	= 0;
	m_hasFourCCSupport	= FALSE;
	m_colorConvert = NULL;
	m_MonitorBeginPosX = 0;
	m_MonitorBeginPosY = 0;
}

DDOffscreenRender::~DDOffscreenRender()
{
	clean();
}

void DDOffscreenRender::GetRenderRect(HWND hWnd, RECT &rcRect)
{
	GetClientRect(hWnd, &rcRect);
	// 多显卡模式下，坐标可能为负值
// 	if (rcRect.left<0 && rcRect.top<0 && rcRect.right<0 && rcRect.bottom<0)
// 	{
// 		return ; 
// 	}

	LONG lWidth = rcRect.right - rcRect.left;
	LONG lHeight = rcRect.bottom - rcRect.top;
	
	POINT lPoint;
	lPoint.x = rcRect.left;
	lPoint.y = rcRect.top;
	ClientToScreen(m_hWnd, &lPoint);
	
	rcRect.left	= lPoint.x;
	rcRect.top	= lPoint.y;
	rcRect.right	= lPoint.x + lWidth;
	rcRect.bottom	= lPoint.y + lHeight;
}

int DDOffscreenRender::init(int index, HWND hWnd, int width, int height, draw_callback cb)
{
	m_index		= index;
	m_hWnd		= hWnd;
	m_width		= width;
	m_height	= height;
	m_callback	= cb;

	m_screenWidth = m_screenHeight = 0;

	resize();

	int iret = 0;
	
	m_csRgbSurfaceCritsec.Lock();
	__try
	{
		iret = initDirectDraw();
	}
	__except(0,1)
	{
		iret = 9;//DH_PLAY_CREATE_DDRAW_ERROR
	}
	m_csRgbSurfaceCritsec.UnLock();

	return iret;
}

void DDOffscreenRender::resize()
{
// 获取窗口大小
	//RECT rect;
	//GetWindowRect(m_hWnd, &rect);

	RECT rect;
	GetRenderRect(m_hWnd, rect);

	rect.left -= m_MonitorBeginPosX;
	rect.right -= m_MonitorBeginPosX;
	rect.top -= m_MonitorBeginPosY;
	rect.bottom -= m_MonitorBeginPosY;

	if (rect.right!=m_destRect.right||rect.bottom!=m_destRect.bottom
		||rect.top!=m_destRect.top||rect.left!=m_destRect.left) 
	{
		m_destRect.left	= rect.left;
		m_destRect.top	= rect.top;
		m_destRect.right	= rect.right;
		m_destRect.bottom	= rect.bottom;

		if (m_destRect.left<0&&m_destRect.top<0
			&&m_destRect.right<0&&m_destRect.bottom<0) 
		{
			// 窗口最小化
			dbg_print("Window Minimize.");
		}
		else 
		{
			if (m_pDDSPrimary == NULL) 
			{
				return;
			}	
			
			if (m_callback)
			{
				AutoLock lock(&m_csRgbSurfaceCritsec);

				SAFE_RELEASE(m_pDDRGBSurface);

				DDSURFACEDESC2 ddsd;
				ZeroMemory(&ddsd, sizeof(ddsd));
				ddsd.dwSize = sizeof(ddsd);
				
				//创建一个和显示窗口一样大的offscreen(RGB表面),用于OSD叠加
				ddsd.dwFlags = DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT;
				ddsd.ddsCaps.dwCaps    = DDSCAPS_OFFSCREENPLAIN | DDSCAPS_VIDEOMEMORY;    
				ddsd.dwWidth           = m_destRect.right - m_destRect.left;
				ddsd.dwHeight          = m_destRect.bottom - m_destRect.top;
				ddsd.ddpfPixelFormat   = ddpfPixelFormats[5];// RGB格式
				
				if (FAILED(m_pDD->CreateSurface(&ddsd, &m_pDDRGBSurface, NULL))) {
					int err = GetLastError();
				}
			}
		}
	}
}

int DDOffscreenRender::clean()
{
	int iret = 0;
	__try
	{
		iret = destroyDDObjects();
	}
	__except(0,1)
	{
		iret = -1;
	}

	return iret;
}

bool DDOffscreenRender::GetMonitorBeginPos(int* width, int*height)
{
	HMODULE hUser32;
    hUser32 = GetModuleHandle(TEXT("USER32"));
    if (hUser32)
	{
		*(FARPROC2*)&g_pfnMonitorFromWindow2   = GetProcAddress(hUser32,"MonitorFromWindow");
		*(FARPROC2*)&g_pfnMonitorFromPoint2    = GetProcAddress(hUser32,"MonitorFromPoint");
        *(FARPROC2*)&g_pfnGetSystemMetrics2    = GetProcAddress(hUser32,"GetSystemMetrics");
	}	

	HMONITOR hMonitor = g_pfnMonitorFromWindow2(m_hWnd, MONITOR_DEFAULTTONEAREST);

//	RECT rc;
//	GetWindowRect(m_hWnd, &rc);

	RECT rc;
	GetRenderRect(m_hWnd, rc);
	int ScreenWidth = g_pfnGetSystemMetrics2(SM_CXSCREEN);
	int ScreenHeight = g_pfnGetSystemMetrics2(SM_CYSCREEN);

	*width = 0;
	*height = 0;
	HWNDHMONITOR* hwndmonitor;
	int i=0;
	for(i = 0; i < g_GUID_List.size(); i++)
	{
		hwndmonitor = &g_GUID_List[i];
		if (hMonitor == hwndmonitor->hMonitor)
		{
			POINT pt;
			pt.y = rc.top;
			int j = rc.left;
			if (j  < 0)
			{
				for (; j <= 1280*11; j++)
				{
					pt.x = j;
					HMONITOR ptMonitor = g_pfnMonitorFromPoint2(pt,MONITOR_DEFAULTTONULL);
					if (ptMonitor != hMonitor)
					{
						*width = j-ScreenWidth;
						break;
					}
				}
			}
			else
			{
				for (; j >0; j--)
				{
					pt.x = j;
					HMONITOR ptMonitor = g_pfnMonitorFromPoint2(pt,MONITOR_DEFAULTTONULL);
					if (ptMonitor != hMonitor)
					{
						*width = j+1;
						break;
					}
				}
			}

			pt.x = rc.left;
			int k= rc.top;
			if (k < 0)
			{
				for (; k <=1024*11; k++)
				{
					pt.y = k;
					HMONITOR ptMonitor = g_pfnMonitorFromPoint2(pt,MONITOR_DEFAULTTONULL);
					if (ptMonitor != hMonitor)
					{
						*height = k-ScreenHeight;
						break;
					}
				}		
			}
			else
			{
				for (; k >0; k--)
				{
					pt.y = k;
					HMONITOR ptMonitor = g_pfnMonitorFromPoint2(pt,MONITOR_DEFAULTTONULL);
					if (ptMonitor != hMonitor)
					{
						*height = k+1;
						break;
					}
				}		
			}
		
			break;
		}
	}

	return true;
}
/************************************************************************
 * 初始化DirectDraw的一般步骤
 * 1. 创建DirectDraw对象，COM接口为IID_IDirectDraw7。
 * 2. 设置协作级别，协作级别如果为DDSCL_FULLSCREEN则还要调用SetDisplayMode()
 * 3. 创建主表面
 * 4. 创建后台主绘图表面（OFFSCREEN或者OVERLAY表面）
 * 5. 获取后台主绘图表面的附加翻转表面（可以多个）
 * 6. 如果是窗口模式，那么这里要设置裁剪区域
************************************************************************/
int DDOffscreenRender::initDirectDraw()
{
	int err = 0;
	
	destroyDDObjects();

	HMODULE hUser32;
    hUser32 = GetModuleHandle(TEXT("USER32"));
    if (hUser32)
	{
		*(FARPROC2*)&g_pfnMonitorFromWindow2   = GetProcAddress(hUser32,"MonitorFromWindow");
		*(FARPROC2*)&g_pfnMonitorFromPoint2    = GetProcAddress(hUser32,"MonitorFromPoint");
        *(FARPROC2*)&g_pfnGetSystemMetrics2    = GetProcAddress(hUser32,"GetSystemMetrics");
	}	

	HMONITOR hMonitor = g_pfnMonitorFromWindow2(m_hWnd, MONITOR_DEFAULTTONEAREST);
	HWNDHMONITOR* hwndmonitor;
	int i=0;
	for(i = 0; i < g_GUID_List.size(); i++)
	{
		hwndmonitor = &g_GUID_List[i];
		if (hMonitor == hwndmonitor->hMonitor)
		{	
			break;
		}
	}

	GetMonitorBeginPos(&m_MonitorBeginPosX, &m_MonitorBeginPosY);

	if (FAILED(DirectDrawCreateEx(i == 0 ? NULL : &(g_GUID_List[i].guid), (VOID**)&m_pDD, IID_IDirectDraw7, NULL)))
	{
		err = 9; //DH_PLAY_CREATE_DDRAW_ERROR
		goto err_return;
	}

	// 协作级别，如果是全屏那么协作级别参数为DDSCL_EXCLUSIVE|DDSCL_FULLSCREEN

// 	if (FAILED(m_pDD->SetCooperativeLevel(m_hWnd, DDSCL_SETFOCUSWINDOW))) 
// 	{
// 		err = 9;
// 		goto err_return;
// 	}

	if (FAILED(m_pDD->SetCooperativeLevel(m_hWnd, DDSCL_NORMAL))) 
	{
		err = 9;
		goto err_return;
	}

	// 如果实现全屏显示，那么此处要设置显示模式，如：
	// hr = m_pDD->SetDisplayMode(640,480,8,0,0);

	// 创建主表面，填充表面描述结构体
	DDSURFACEDESC2 ddsd;
	ZeroMemory(&ddsd, sizeof(ddsd));

	ddsd.dwSize = sizeof( ddsd );
	ddsd.dwFlags = DDSD_CAPS;
	ddsd.ddsCaps.dwCaps = DDSCAPS_PRIMARYSURFACE;

	//创建主表面    
	if ( FAILED(m_pDD->CreateSurface(&ddsd, &m_pDDSPrimary, NULL))) 
	{
		err = 9;
		goto err_return;
	}

	// 检查能力
	DDCAPS ddCaps;
	ZeroMemory(&ddCaps, sizeof(DDCAPS));
	ddCaps.dwSize = sizeof(DDCAPS);

	if (FAILED(m_pDD->GetCaps(&ddCaps,NULL)))
	{
		err = 9;
		goto err_return;
	}

	if (ddCaps.dwCaps&DDCAPS_BLT
		&&ddCaps.dwCaps&DDCAPS_BLTFOURCC
		&&ddCaps.dwFXCaps&DDFXCAPS_BLTSHRINKX
		&&ddCaps.dwFXCaps&DDFXCAPS_BLTSHRINKY
		&&ddCaps.dwFXCaps&DDFXCAPS_BLTSTRETCHX
		&&ddCaps.dwFXCaps&DDFXCAPS_BLTSTRETCHY)
	{
		dbg_print("SUPPORT BLT-STRETCH/SHRINK and BLT-FOURCC\n");
	} 
	else
	{
		err = 9;
		goto err_return;
	}

	m_pDD->GetDisplayMode(&ddsd);

	m_screenWidth = ddsd.dwWidth;
	m_screenHeight = ddsd.dwHeight;

// 	// 创建绘图表面
	if (FAILED(createDrawSurface())) 
	{
		err = 9;
		goto err_return;
	}

	err_return:
	return err;
}

HRESULT DDOffscreenRender::createDrawSurface()
{
	DDSURFACEDESC2 ddsd;
	DDSCAPS2       ddscaps;
	HRESULT		   hr;

	SAFE_RELEASE(m_pDDSVideoSurface); 

	// 创建主绘图表面,可以是离屏表面或者是Overlay表面
	ZeroMemory(&ddsd, sizeof(ddsd) );
	ddsd.dwSize = sizeof(ddsd);

	ddsd.dwFlags = DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT;
	ddsd.ddsCaps.dwCaps    = DDSCAPS_OFFSCREENPLAIN | DDSCAPS_VIDEOMEMORY;
	ddsd.dwWidth           = m_width;
	ddsd.dwHeight          = m_height;

	int i = 0;

	while (i < 3)
	{
		ddsd.ddpfPixelFormat   = ddpfPixelFormats[i];
		hr = m_pDD->CreateSurface(&ddsd, &m_pDDSVideoSurface, NULL);
		if (FAILED(hr))
		{ 
			i++;
		} 
		
		else
		{
			break;
		}
		
	}
	
	if (i < 3) 
	{
		m_colorConvert = ccfunc[i];
	} 
	
	else 
	{
		return hr;
	}
 	
	ZeroMemory(&ddscaps, sizeof(ddscaps));
	ddscaps.dwCaps = DDSCAPS_BACKBUFFER;

	if (m_callback)
	{
		//创建一个和显示窗口一样大的offscreen(RGB表面),用于OSD叠加
		ddsd.dwWidth  = m_destRect.right - m_destRect.left;
		ddsd.dwHeight = m_destRect.bottom - m_destRect.top;
		if(ddsd.dwWidth == 0)
		{
			ddsd.dwWidth = m_width;
			ddsd.dwHeight = m_height;
		}
		ddsd.ddpfPixelFormat   = ddpfPixelFormats[5];// RGB格式

		if (FAILED(hr = m_pDD->CreateSurface(&ddsd, &m_pDDRGBSurface, NULL)))
		{
			return hr;
		}	
	}

	LPDIRECTDRAWCLIPPER pClipper = NULL;

	if(FAILED(hr = m_pDD->CreateClipper(0, &pClipper, NULL))) 
	{
		return hr;
	}
	
	if(FAILED(hr = pClipper->SetHWnd(0, m_hWnd)))
	{
		return hr;
	}

	if(FAILED( hr = m_pDDSPrimary->SetClipper(pClipper))) 
	{
		return hr;
	}

	SAFE_RELEASE(pClipper);

	return S_OK;
}

HRESULT DDOffscreenRender::destroyDDObjects()
{
	if (m_callback)
	{
		AutoLock lock(&m_csRgbSurfaceCritsec);
		SAFE_RELEASE(m_pDDRGBSurface);
	}
	
	SAFE_RELEASE(m_pDDSVideoSurface);
	SAFE_RELEASE(m_pDDSPrimary);

	if (m_pDD) 
	{
		m_pDD->SetCooperativeLevel(m_hWnd, DDSCL_NORMAL);
	}

	SAFE_RELEASE( m_pDD );

	return S_OK;
}

BOOL DDOffscreenRender::hasFourCCSupport(LPDIRECTDRAWSURFACE7 lpdds)
{
	DDSURFACEDESC2 ddsd;

	ZeroMemory(&ddsd, sizeof(ddsd));
	ddsd.dwSize = sizeof(ddsd);

	lpdds->GetSurfaceDesc(&ddsd);

	if (ddsd.ddpfPixelFormat.dwFlags == DDPF_FOURCC)
		return TRUE;

	return FALSE;
}

int DDOffscreenRender::render(unsigned char *py, unsigned char *pu, unsigned char *pv, int width, int height,RECT*srcRect)
{
	LONG lWidth = 0; 
	LONG lHeight = 0; 

	if (py==0||pu==0||pv==0||width<=0||height<=0)
	{
		goto user_draw;
	}

	if ((width != m_width)||(height != m_height)||m_pDDSVideoSurface==NULL||m_pDDSPrimary==NULL) 
	{
		dbg_print("handle = %d,Render Video (Resize).",m_index);
		
		clean();
		
		m_width = width;
		m_height = height;
		
		int ret = init(m_index, m_hWnd, m_width, m_height, m_callback);
		if ( ret > 0) 
		{
			return ret;
		}
	}

	HRESULT hr;

	RECT rect;
	GetRenderRect(m_hWnd, rect);
	// 多显卡模式下，坐标可能为负值
// 	if (rect.left < 0 && rect.right < 0 && rect.top < 0 && rect.bottom < 0)
// 	{
// 		return 0;
// 	}

 	resize();

	//图像移动到另一位置
	if (m_destRect.right < 0 || m_destRect.left > m_screenWidth || m_destRect.bottom < 0 || m_destRect.top > (50+m_screenHeight))
	{
		clean();
		int ret = init(m_index, m_hWnd, m_width, m_height, m_callback);
		if ( ret > 0) 
		{
			return ret;
		}
		return 0;
	}

	// 如果图像最小化
	if (m_destRect.right-m_destRect.left <= 1 || m_destRect.bottom-m_destRect.top <= 1 ||
		(m_destRect.left <= 0 && m_destRect.right <= 0 && m_destRect.top <= 0 && m_destRect.bottom <= 0))
	{
		return 0;
	}
	
	DDSURFACEDESC2 ddsd;
	ZeroMemory(&ddsd, sizeof(ddsd));
	ddsd.dwSize = sizeof(ddsd);
	
	hr = m_pDDSVideoSurface->Lock(NULL, &ddsd, DDLOCK_SURFACEMEMORYPTR|DDLOCK_WAIT, NULL);

	if(hr == DDERR_SURFACELOST)
	{
		hr = m_pDDSPrimary->Restore();
		m_pDDSVideoSurface->Restore();

		if (m_callback)
		{
			m_csRgbSurfaceCritsec.Lock();
			m_pDDRGBSurface->Restore();
			m_csRgbSurfaceCritsec.UnLock();
		}	

		if (hr == DDERR_WRONGMODE)
		{
			clean();

			int ret = init(m_index, m_hWnd, m_width, m_height, m_callback);
			if ( ret > 0) 
			{
				return ret;
			}
			return 0;
		}

		hr = m_pDDSVideoSurface->Unlock(NULL);
		return 0;
	}

	if (ddsd.lpSurface == 0)
	{
#ifdef _DEBUG
		char str[120];
		sprintf(str, "m_pDDSVideoSurface->Lock error 0x%x!!!!!!!!!\n", hr);
		OutputDebugString(str);
#endif
		return 24; //DH_PLAY_VIDEOSURFACE_LOCK_ERROR
	}

	__try
	{
		m_colorConvert(py, pu, pv, (unsigned char *)ddsd.lpSurface, ddsd.lPitch, ddsd.dwWidth, ddsd.dwHeight);
	}
	__except(0,1)
	{		
		hr = m_pDDSVideoSurface->Unlock(NULL);
 		return 0;
	}

//	m_colorConvert(py, pu, pv, (unsigned char *)ddsd.lpSurface, ddsd.lPitch, ddsd.dwWidth, ddsd.dwHeight);
	
	hr = m_pDDSVideoSurface->Unlock(NULL);

	if (m_bVerticalSyncEnable)
	{
		m_pDD->WaitForVerticalBlank(DDWAITVB_BLOCKBEGIN, NULL);
	}

	if (m_callback) 
	{
		m_csRgbSurfaceCritsec.Lock();
		if ( NULL != m_pDDRGBSurface )
		{
			hr = m_pDDRGBSurface->Blt(NULL, m_pDDSVideoSurface,srcRect,0,0);
			HDC dc = NULL;
			hr = m_pDDRGBSurface->GetDC( &dc );
			m_callback(m_index , dc);
			hr = m_pDDRGBSurface->ReleaseDC( dc );
		}
		//hr = m_pDDSPrimary->Flip(NULL, DDFLIP_WAIT);
		//hr = m_pDDSPrimary->Blt(&m_destRect, m_pDDRGBSurface, NULL, 0, 0);
		hr = m_pDDSPrimary->Blt(&m_destRect, m_pDDRGBSurface, NULL, DDBLT_ASYNC, 0);
		m_csRgbSurfaceCritsec.UnLock();
	}
	else
	{
		hr = m_pDDSPrimary->Blt(&m_destRect, m_pDDSVideoSurface, srcRect, DDBLT_ASYNC, 0);
	}
	
	if (hr == DDERR_SURFACELOST)
	{
		m_pDDSPrimary->Restore();
		m_pDDSVideoSurface->Restore();

		if (m_callback) 
		{
			m_csRgbSurfaceCritsec.Lock();
			m_pDDRGBSurface->Restore();
			m_csRgbSurfaceCritsec.UnLock();
		}
	}

user_draw:
	return hr;
}































































