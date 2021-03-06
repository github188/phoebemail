using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Collections;

namespace LazyAccountant
{
    public partial class MainForm : Form
    {
        private ListViewItem m_currentListViewItem = null;
        
        public MainForm()
        {
            InitializeComponent();

            //打开软件, 默认是计算上个月的工资数据
            DateTime lastMonth = DateTime.Now.AddMonths(-1);

            numericUpDownTargetYear.Value = (decimal)lastMonth.Year;
            numericUpDownTargetMonth.Value = (decimal)lastMonth.Month;

            //载入个税起征点
            numericUpDownTaxStartPoint.Value = (decimal)DataCenter.Instance.IndividualIncomeTaxStart;
        }

        enum Field
        {
            Name,
            Allowance,
            Late,
            Absent,
            PreviousTaxCut,
            OtherCut,
            Receipt
        }

        private string BuildFileName(int year, int month, string ext)
        {
            if (!Directory.Exists("data"))
            {
                Directory.CreateDirectory("data");
            }
            return string.Format(@"data\{0}{1:00}.{2}", year, month, ext);
        }

        private string BuildFileName(string ext)
        {
            if (!Directory.Exists("data"))
            {
                Directory.CreateDirectory("data");
            }
            DateTime targetMonth = GetTargetMonth();
            return string.Format(@"data\{0}{1:00}.{2}", targetMonth.Year, targetMonth.Month, ext);
        }

        private string BuildSalariesFileName(int year, int month)
        {
            return BuildFileName(year, month, "sa");
        }

        private string BuildSalariesFileName()
        {
            return BuildFileName("sa");
        }

        private string BuildCalcArgsFileName(int year, int month)
        {
            return BuildFileName(year, month, "ca");
        }

        private string BuildCalcArgsFileName()
        {
            return BuildFileName("ca");
        }

        private string BuildReceiptFileName(int year, int month)
        {
            return BuildFileName(year, month, "txt");
        }

        private string BuildReceiptFileName()
        {
            return BuildFileName("txt");
        }


        private void MainForm_Load(object sender, EventArgs e)
        {          
            //加载计薪参数
            DateTime targetMonth = GetTargetMonth();
            string file = BuildCalcArgsFileName(targetMonth.Year, targetMonth.Month);
            Dictionary<string, CalcArg> calcArgs = DataCenter.Instance.LoadCalcArgs(file);

            if (calcArgs.Count == 0)
            {
                //准备好上个月的数据
                DateTime lastMonth = targetMonth.AddMonths(-1);
                string fileOfLastMonth = BuildSalariesFileName(lastMonth.Year, lastMonth.Month);
                Dictionary<string, Salary> salariesOfLastMonth = DataCenter.Instance.LoadSalaries(fileOfLastMonth);//可能没查到, 空列表

                foreach (Salary s in salariesOfLastMonth.Values)
                {
                    CalcArg c = new CalcArg();
                    c.m_employeeId = s.m_args.m_employeeId;
                    c.m_previousTaxCut = s.m_taxToCut;
                    calcArgs.Add(s.m_args.m_employeeId, c);
                }
            }

            foreach (Employee employee in DataCenter.Instance.Employees.Values)
            {
                Salary s = new Salary();
                s.m_employee = employee;

                if (calcArgs.ContainsKey(employee.m_id))
                {
                    s.m_args = calcArgs[employee.m_id];
                }
                else
                {
                    s.m_args = new CalcArg();
                    s.m_args.m_employeeId = s.m_employee.m_id;
                }

                ListViewItem item = new ListViewItem(BuildInfo(s));
                item.Tag = s;
                listView1.Items.Add(item);
            }

            if (listView1.Items.Count > 0)
            {
                m_currentListViewItem = listView1.Items[0];
            }
        }

        private String[] BuildInfo(Salary s)
        {
            string[] infos = new string[8];
            infos[(int)Field.Name] = s.m_employee.m_name;
            infos[(int)Field.Allowance] = s.m_args.m_allowance.ToString("0.00");
            infos[(int)Field.Late] = s.m_args.m_late.ToString();
            infos[(int)Field.Absent] = s.m_args.m_absent.ToString("0.00");
            infos[(int)Field.PreviousTaxCut] = s.m_args.m_previousTaxCut.ToString("0.00");
            infos[(int)Field.OtherCut] = s.m_args.m_otherCut.ToString("0.00");
            infos[(int)Field.Receipt] = s.ToString();
            return infos;
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            OnSelectedEmployeeChanged();
        }

        private void OnSelectedEmployeeChanged()
        {
            try
            {
                if (listView1.SelectedItems.Count > 0)
                {
                    m_currentListViewItem = listView1.SelectedItems[0];
                    Salary s = (Salary)m_currentListViewItem.Tag;
                    CalcArg args = s.m_args;
                    numericUpDownAllowance.Value = args.m_allowance;
                    numericUpDownLate.Value = args.m_late;
                    numericUpDownAbsent.Value = (decimal)args.m_absent;
                    numericUpDownPreviousTaxCut.Value = args.m_previousTaxCut;
                    numericUpDownOtherCut.Value = args.m_otherCut;
                    richTextBoxReceipt.Text = m_currentListViewItem.SubItems[(int)Field.Receipt].Text;
                }
            }
            catch (System.Exception ex)
            {
                QMessageBox.ShowError(ex.ToString());
            }
        }
        private void numericUpDownAllowance_ValueChanged(object sender, EventArgs e)
        {
            if (m_currentListViewItem != null)
            {
                Salary s = (Salary)m_currentListViewItem.Tag;
                CalcArg args = s.m_args;
                args.m_allowance = numericUpDownAllowance.Value;
                m_currentListViewItem.SubItems[(int)Field.Allowance].Text = args.m_allowance.ToString("0.00");
            }
        }

        private void numericUpDownLate_ValueChanged(object sender, EventArgs e)
        {
            if (m_currentListViewItem != null)
            {
                Salary s = (Salary)m_currentListViewItem.Tag;
                CalcArg args = s.m_args;
                args.m_late = (int)numericUpDownLate.Value;
                m_currentListViewItem.SubItems[(int)Field.Late].Text = args.m_late.ToString();
            }
        }

        private void numericUpDownAbsent_ValueChanged(object sender, EventArgs e)
        {
            if (m_currentListViewItem != null)
            {
                Salary s = (Salary)m_currentListViewItem.Tag;
                CalcArg args = s.m_args;
                args.m_absent = (float)numericUpDownAbsent.Value;
                m_currentListViewItem.SubItems[(int)Field.Absent].Text = args.m_absent.ToString("0.00");
            }
        }

        private void numericUpDownPreviousTaxCut_ValueChanged(object sender, EventArgs e)
        {
            if (m_currentListViewItem != null)
            {
                Salary s = (Salary)m_currentListViewItem.Tag;
                CalcArg args = s.m_args;
                args.m_previousTaxCut = numericUpDownPreviousTaxCut.Value;
                m_currentListViewItem.SubItems[(int)Field.PreviousTaxCut].Text = args.m_previousTaxCut.ToString("0.00");
            }
        }

        private void numericUpDownOtherCut_ValueChanged(object sender, EventArgs e)
        {
            if (m_currentListViewItem != null)
            {
                Salary s = (Salary)m_currentListViewItem.Tag;
                CalcArg args = s.m_args;
                args.m_otherCut = numericUpDownOtherCut.Value;
                m_currentListViewItem.SubItems[(int)Field.OtherCut].Text = args.m_otherCut.ToString("0.00");
            }
        }

        private void buttonCalcTax_Click(object sender, EventArgs e)
        {
            try
            {
                decimal start = numericUpDownTaxStartPoint.Value;
                decimal amount = numericUpDownTaxAmount.Value;
                numericUpDownTax.Value = IndividualIncomeTax.GetTax(start, amount);
            }
            catch (System.Exception ex)
            {
                QMessageBox.ShowError(ex.ToString());
            }
        }

        private void buttonCalcSalary_Click(object sender, EventArgs e)
        {
            try
            {
                this.Enabled = false;

                int workdayCount = (int)numericUpDownWorkDayCount.Value;
                if (checkBoxSelectedOnly.Checked)
                {
                    CalcSalary(m_currentListViewItem, workdayCount);
                }
                else
                {
                    foreach (ListViewItem item in listView1.Items)
                    {
                        CalcSalary(item, workdayCount);
                    }
                }

                UpdateTotalInfo();

                OnSelectedEmployeeChanged();

                buttonSaveResult.Enabled = true;
                buttonSendMail.Enabled = true;
                buttonExport.Enabled = true;
            }
            catch (System.Exception ex)
            {
                QMessageBox.ShowError(ex.ToString());
            }
            finally
            {
                this.Enabled = true;
            }
        }

        private void buttonSendMail_Click(object sender, EventArgs e)
        {
            try
            {
                this.Enabled = false;
                labelProgress.Visible = true;
                StringBuilder sb = new StringBuilder();
                ArrayList c = new ArrayList();
                if (checkBoxSelectedOnly.Checked)
                {
                    c.Add(m_currentListViewItem);
                }
                else
                {
                    c.AddRange(listView1.Items);
                }
                SendMail(c);
            }
            catch (System.Exception ex)
            {
                QMessageBox.ShowError(ex.ToString());
            }
            finally
            {
                this.Enabled = true;
                labelProgress.Visible = false;
            }

        }

        private void SendMail(ArrayList items)
        {
            Email email = new Email();
            StringBuilder sb = new StringBuilder();
            String subject = String.Format("{0}年{1}月工资单", (int)numericUpDownTargetYear.Value, (int)numericUpDownTargetMonth.Value);
            int i = 1;
            foreach (ListViewItem item in items)
            {
                string receipt = item.SubItems[(int)Field.Receipt].Text;

                if (!string.IsNullOrEmpty(receipt))
                {
                    Salary s = (Salary)item.Tag;
                    CalcArg args = s.m_args;
                    Employee emp = s.m_employee;
                    string emailAddress = emp.m_email;
                    if (!string.IsNullOrEmpty(emailAddress))
                    {
                        labelProgress.Text = string.Format("正在发送{0}/{1}...", i, items.Count);
                        this.Refresh();
                        email.SendMail(emailAddress, subject, receipt);
                        sb.AppendLine(emailAddress);
                        ++i;
                    }
                }
            }
            QMessageBox.ShowInfomation("已发送邮件到下列账号:\n" + sb.ToString());
        }

        private void buttonSaveResult_Click(object sender, EventArgs e)
        {
            try
            {
                this.Enabled = false;

                List<Salary> salaries = new List<Salary>();

                foreach (ListViewItem item in listView1.Items)
                {
                    salaries.Add((Salary)item.Tag);
                }
                string file = BuildSalariesFileName((int)numericUpDownTargetYear.Value, (int)numericUpDownTargetMonth.Value);
                DataCenter.Instance.SaveSalaries(file, salaries);
                QMessageBox.ShowInfomation(String.Format("数据已保存至{0}", file));
            }
            catch (System.Exception ex)
            {
                QMessageBox.ShowError(ex.ToString());
            }
            finally
            {
                this.Enabled = true;
            }
        }

        private void CalcSalary(ListViewItem item, int workdayCount)
        {
            Salary s = (Salary)item.Tag;
            s.Calc(workdayCount);
            item.SubItems[(int)Field.Receipt].Text = s.ToString();
        }

        private void numericUpDownTargetYear_ValueChanged(object sender, EventArgs e)
        {
            UpdateWorkdayCount();
        }

        private void numericUpDownTargetMonth_ValueChanged(object sender, EventArgs e)
        {
            UpdateWorkdayCount();
        }

        //当目标月份改变时, 一是要计算出这个月的工作天数, 另外要加载对应的工资数据, 没有数据就创建数据
        private void UpdateWorkdayCount()
        {
            try
            {
                numericUpDownWorkDayCount.Value = (decimal)GetWorkdayCount();
            }
            catch (System.Exception ex)
            {
                QMessageBox.ShowError(ex.ToString());
            }
        }

        //从界面上获取当前指向的月份
        private DateTime GetTargetMonth()
        {
            return new DateTime((int)numericUpDownTargetYear.Value, (int)numericUpDownTargetMonth.Value, 1);
        }

        private int GetWorkdayCount()
        {
            return Salary.GetWorkdayCount(GetTargetMonth());
        }

        //每次退出的时候, 保存已经输入的数据
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                SaveCalcArgs();
            }
            catch (System.Exception ex)
            {
                QMessageBox.ShowError(ex.ToString());
            }
        }

        private void SaveCalcArgs()
        {
            List<CalcArg> calcArgs = new List<CalcArg>();
            foreach (ListViewItem item in listView1.Items)
            {
                Salary s = (Salary)item.Tag;
                calcArgs.Add(s.m_args);
            }

            DateTime m = GetTargetMonth();
            string file = BuildCalcArgsFileName(m.Year, m.Month);
            DataCenter.Instance.SaveCalcArgs(file, calcArgs);
        }

        private void UpdateTotalInfo()
        {
            int late = 0;
            float absent = 0.0F;
            decimal tax = 0.0M;
            decimal totalInternal = 0.0M;
            decimal totalExternal = 0.0M;
            decimal total = 0.0M;
            foreach (ListViewItem item in listView1.Items)
            {
                Salary s = (Salary)item.Tag;
                late += s.m_args.m_late;
                absent += s.m_args.m_absent;
                tax += s.m_args.m_previousTaxCut;
                totalInternal += s.m_internalIncome;
                totalExternal += s.m_externalIncome;
                total += s.m_totalIncome;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("迟到: {0}     缺勤: {1:0.00}", late, absent);
            sb.AppendFormat("    个税: {0:0.00}", tax);
            sb.AppendFormat("\n账内: {0:0.00}", totalInternal);
            sb.AppendFormat("    帐外: {0:0.00}", totalExternal);
            sb.AppendFormat("    总计: {0:0.00}", total);

            this.labelStat.Text = sb.ToString();
        }

        private void buttonExport_Click(object sender, EventArgs e)
        {
            string file = BuildReceiptFileName();
            if (File.Exists(file))
            {
                File.Delete(file);
            }

            using (StreamWriter sw = File.CreateText(file))
            {
                foreach (ListViewItem item in listView1.Items)
                {
                    Salary s = (Salary)item.Tag;
                    sw.WriteLine("******************************************");
                    sw.WriteLine(s.ToString());
                }

                sw.WriteLine("汇总信息(可直接复制粘贴至excel):");

                foreach (ListViewItem item in listView1.Items)
                {
                    Salary s = (Salary)item.Tag;
                    sw.WriteLine(s.ToExcelLine());
                }
            }

            System.Diagnostics.Process.Start("notepad.exe", file);
        }
    }
}