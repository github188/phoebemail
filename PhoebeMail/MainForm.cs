using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Net.Mail;
using System.Net;
using PhoebeMail.Properties;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections;

namespace PhoebeMail
{
    public partial class MainForm : Form
    {
        private string m_email = "email.txt";
        private string m_formText;
        private string m_buttonText;
        private static readonly String m_caption = "PhoebeMail";

        private System.Timers.Timer m_timer = new System.Timers.Timer();

        public MainForm()
        {
            InitializeComponent();
            m_formText = Text;
            m_buttonText = buttonSend.Text;

            m_timer.AutoReset = false;//只发一次
            m_timer.Enabled = false;
            m_timer.Elapsed += new System.Timers.ElapsedEventHandler(Timeout);
        }

        public static void ShowInfomation(String message)
        {
            MessageBox.Show(message, m_caption + " Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void ShowWarning(String message)
        {
            MessageBox.Show(message, m_caption + " Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static void ShowError(String message)
        {
            MessageBox.Show(message, m_caption + " Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public static DialogResult ShowQuestion(String message)
        {
            return MessageBox.Show(message, m_caption + " Question", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            textBoxSubject.Text = textBoxDefaultSubject.Text;

            if (File.Exists(m_email))
            {
                richTextBoxDefaultBody.Text = File.ReadAllText(m_email);
                richTextBoxBody.Text = richTextBoxDefaultBody.Text;
            }
            DateTime now = DateTime.Now;

            dateTimePickerTimedSend.Value = new DateTime(now.Year, now.Month, now.Day, 21, 0, 0);

            LoadAccounts();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Settings.Default.Username = textBoxUsername.Text;
            Settings.Default.Password = textBoxPassword.Text;
            Settings.Default.Nickname = textBoxNickname.Text;
            Settings.Default.Server = textBoxServer.Text;
            Settings.Default.Port = numericUpDownPort.Value;
            Settings.Default.Ssl = checkBoxSsl.Checked;
            Settings.Default.DefaultSubject = textBoxDefaultSubject.Text;
            Settings.Default.Save();

            SaveAccounts();
        }

        private void buttonQuit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void textBoxDefaultSubject_TextChanged(object sender, EventArgs e)
        {
            textBoxSubject.Text = textBoxDefaultSubject.Text;
        }

        private void richTextBoxDefaultBody_TextChanged(object sender, EventArgs e)
        {
            if (!String.IsNullOrEmpty(richTextBoxDefaultBody.Text))
            {
                richTextBoxBody.Text = richTextBoxDefaultBody.Text;
                File.WriteAllText(m_email, richTextBoxDefaultBody.Text);
            }
        }

        private void clearAllAddressToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBoxAddresses.Items.Clear();
        }

        private void clearAllAttachmentToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBoxAttachments.Items.Clear();
        }

        private void importToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OnImportAddress();
        }

        private void OnImportAddress()
        {
            DialogResult r = openFileDialog1.ShowDialog();
            if (r == DialogResult.OK)
            {
                if (File.Exists(openFileDialog1.FileName))
                {
                    string text = File.ReadAllText(openFileDialog1.FileName);

                    MatchCollection emails = GetEmailAddress(text);
                    listBoxAddresses.Items.Clear();
                    foreach (Match m in emails)
                    {
                        if (!listBoxAddresses.Items.Contains(m.Value))
                        {
                            listBoxAddresses.Items.Add(m.Value);
                        }
                    }
                }
            }
        }
        private void exportToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DialogResult r = saveFileDialog1.ShowDialog();
            if (r == DialogResult.OK)
            {
                using (StreamWriter sw = File.CreateText(saveFileDialog1.FileName))
                {
                    foreach (object item in listBoxAddresses.Items)
                    {
                        sw.WriteLine(item.ToString());
                    }
                }
            }
        }

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (int index in listBoxAddresses.SelectedIndices)
            {
                listBoxAddresses.Items.RemoveAt(index);
            }
        }

        private Regex m_regex = new Regex(@"\w+([-+.]\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*");

        private bool IsEmailAddress(String email)
        {
            return m_regex.IsMatch(email);
        }

        private MatchCollection GetEmailAddress(string input)
        {
            return m_regex.Matches(input);
        }

        class MailJob
        {
            public SmtpClient m_smtpClient;
            public MailMessage m_mailMessage;
            public object[] m_addresses;
            public DateTime m_begin;
            public DateTime m_end;

            public int m_done = 0;
            public int m_fail = 0;
            public string m_current = string.Empty;

            public MailJob(SmtpClient client, MailMessage msg, object[] addresses)
            {
                m_smtpClient = client;
                m_mailMessage = msg;
                m_addresses = addresses;
            }

            public void Send(BackgroundWorker worker)
            {
                m_begin = DateTime.Now;
                foreach (object item in m_addresses)
                {
                    if (worker.CancellationPending)
                    {
                        break;
                    }

                    m_current = item.ToString();
                    m_mailMessage.To.Clear();
                    m_mailMessage.To.Add(m_current);

                    try
                    {
                        worker.ReportProgress((int)(m_done / m_addresses.Length), this);
                        m_smtpClient.Send(m_mailMessage);
                    }
                    catch (System.Exception)
                    {
                        ++m_fail;
                    }
                    finally
                    {
                        ++m_done;
                    }
                }
                m_end = DateTime.Now;
            }
        }

        private MailJob job = null;

        private void buttonSend_Click(object sender, EventArgs e)
        {
            if (m_timer.Enabled)//表示timer正在工作
            {
                m_timer.Stop();
                m_timer.Enabled = false;
                OnDone();
                return;
            }
            else if (backgroundWorkerMailSender.IsBusy)
            {
                backgroundWorkerMailSender.CancelAsync();
                OnDone();
                return;
            }

            if (comboBoxAccounts.SelectedItem == null)
            {
                ShowWarning("account can not be empty!");
                return;
            }
            else if (String.IsNullOrEmpty(textBoxSubject.Text))
            {
                ShowWarning("email subject can not be empty!");
                return;
            }
            else if (String.IsNullOrEmpty(richTextBoxBody.Text))
            {
                ShowWarning("email body can not be empty!");
                return;
            }
            else if (listBoxAddresses.Items.Count <= 0)
            {
                ShowWarning("recipients can not be empty!");
                return;
            }

            TagedItem item = (TagedItem)comboBoxAccounts.SelectedItem;
            ListViewItem lvi = item.item;
            AccountForm f = (AccountForm)lvi.Tag;

            SmtpClient smtpClient = new SmtpClient();
            smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;
            smtpClient.EnableSsl = f.SslEnabled();
            smtpClient.Credentials = new NetworkCredential(f.GetUsername(), f.GetPassword());
            smtpClient.Host = f.GetServer();
            smtpClient.Port = f.GetPort();

            MailMessage mailMessage = new MailMessage();
            mailMessage.From = new MailAddress(f.GetUsername(), f.GetNickname());
            mailMessage.SubjectEncoding = Encoding.GetEncoding("GB2312");
            mailMessage.BodyEncoding = Encoding.GetEncoding("GB2312");
            mailMessage.Subject = textBoxSubject.Text;
            mailMessage.Body = richTextBoxBody.Text;

            object[] addresses = new object[listBoxAddresses.Items.Count];

            listBoxAddresses.Items.CopyTo(addresses, 0);

            job = new MailJob(smtpClient, mailMessage, addresses);

            if (checkBoxTimedSend.Checked)
            {
                if (dateTimePickerTimedSend.Value <= DateTime.Now)
                {
                    ShowError("the time is in the past!");
                    return;
                }
                m_timer.Interval = dateTimePickerTimedSend.Value.Subtract(DateTime.Now).TotalSeconds * 1000;
            }
            else
            {
                m_timer.Interval = 1;
            }

            m_timer.Enabled = true;
            m_timer.Start();

            buttonSend.Text = "Cancel";
            buttonQuit.Enabled = false;

            this.Text = String.Format("{0} - waiting to send...", m_formText);
        }

        private void backgroundWorkerMailSender_DoWork(object sender, DoWorkEventArgs e)
        {
            job.Send(backgroundWorkerMailSender);
        }

        private void backgroundWorkerMailSender_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            this.Invoke(new ProgressChangedEventHandler(UpdateProgress));

        }

        private delegate void ProgressChangedEventHandler();

        private void UpdateProgress()
        {
            this.Text = String.Format("{0} - {1}/{2} done, {3} fail. sending to {4}...", m_formText, job.m_done, job.m_addresses.Length, job.m_fail, job.m_current);
            this.Update();
        }

        private void backgroundWorkerMailSender_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            this.Invoke(new ProgressChangedEventHandler(OnDone));
        }

        private void OnDone()
        {
            Text = m_formText;
            buttonSend.Text = m_buttonText;
            buttonQuit.Enabled = true;

            StringBuilder sum = new StringBuilder();
            sum.AppendFormat("send mail finished, total = {0}, done = {1}, fail = {2}\n", job.m_addresses.Length, job.m_done, job.m_fail);
            sum.AppendFormat("begin at {0}\n", job.m_begin.ToString("yyyy-MM-dd HH:mm:ss"));
            sum.AppendFormat("end   at {0}\n", job.m_end.ToString("yyyy-MM-dd HH:mm:ss"));

            if (job.m_done > 0)
            {
                TimeSpan span = job.m_end.Subtract(job.m_begin);
                int seconds = (int)(span.TotalSeconds + 0.5);
                sum.AppendFormat("average {0}/{1} = {2} seconds\n", seconds, job.m_done, seconds / job.m_done);
            }

            ShowInfomation(sum.ToString());
        }

        private void Timeout(Object sender, System.Timers.ElapsedEventArgs e)
        {
            backgroundWorkerMailSender.RunWorkerAsync();
            m_timer.Enabled = false;
            m_timer.Stop();
        }

        private void checkBoxTimedSend_CheckedChanged(object sender, EventArgs e)
        {
            dateTimePickerTimedSend.Enabled = checkBoxTimedSend.Checked;
        }

        private void addToolStripMenuItem_Click(object sender, EventArgs e)
        {
            EmailForm form = new EmailForm();
            DialogResult r = form.ShowDialog();
            if (r == DialogResult.OK)
            {
                string address = form.GetAddress();

                if (!IsEmailAddress(address))
                {
                    ShowWarning("invalid email address!");
                }
                else if (listBoxAddresses.Items.Contains(address))
                {
                    ShowWarning("email address existed!");
                }
                else
                {
                    listBoxAddresses.Items.Add(form.GetAddress());
                }
            }
        }

        private void editToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OnEditAddress();
        }

        private void listBoxAddresses_MouseDoubleClick(object sender, MouseEventArgs e)
        {

            if (listBoxAddresses.SelectedIndices.Count > 0)
            {
                OnEditAddress();
            }
            else
            {
                OnImportAddress();
            }
        }

        private void OnEditAddress()
        {
            if (listBoxAddresses.SelectedIndices.Count > 0)
            {
                EmailForm form = new EmailForm();
                string old = listBoxAddresses.SelectedItems[0].ToString();
                form.SetAddress(old);
                DialogResult r = form.ShowDialog();
                if (r == DialogResult.OK)
                {
                    string newAddress = form.GetAddress();

                    if (!IsEmailAddress(newAddress) && (newAddress != old))
                    {
                        ShowWarning("invalid email address!");
                    }
                    else if (listBoxAddresses.Items.Contains(newAddress))
                    {
                        ShowWarning("email address existed!");
                    }
                    else if (newAddress == old)
                    {
                        //Do Nothing
                    }
                    else
                    {
                        listBoxAddresses.Items.RemoveAt(listBoxAddresses.SelectedIndices[0]);
                        listBoxAddresses.Items.Add(newAddress);
                    }
                }
            }
        }

        private void addAccountToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AccountForm f = new AccountForm();
            while (f.ShowDialog() == DialogResult.OK)
            {
                string username = f.GetUsername();

                if (string.IsNullOrEmpty(username))
                {
                    ShowWarning("username can't be empty!");
                }
                else if (!IsEmailAddress(username))
                {
                    ShowWarning("email address is not valid!");
                }
                else if (string.IsNullOrEmpty(f.GetPassword()))
                {
                    ShowWarning("password can't be empty!");
                }
                else if (string.IsNullOrEmpty(f.GetNickname()))
                {
                    ShowWarning("nickname can't be empty!");
                }
                else if (string.IsNullOrEmpty(f.GetServer()))
                {
                    ShowWarning("server can't be empty!");
                }
                else
                {
                    ListViewItem item = new ListViewItem();
                    item.Text = f.GetUsername();
                    StringBuilder b = new StringBuilder();
                    foreach (char c in f.GetPassword())
                    {
                        b.Append("*");
                    }
                    item.SubItems.Add(b.ToString());
                    item.SubItems.Add(f.GetNickname());
                    item.SubItems.Add(f.GetServer());
                    item.SubItems.Add(f.GetPort().ToString());
                    item.SubItems.Add(f.SslEnabled().ToString());
                    item.Tag = f;//为方便， 先这样
                    listViewAccounts.Items.Add(item);
                    //add success.
                    break;
                }
            }
        }

        private void deleteAccountToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (int index in listViewAccounts.SelectedIndices)
            {
                listViewAccounts.Items.RemoveAt(index);
            }
        }

        private void clearAccountToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listViewAccounts.Items.Clear();
        }

        private void editAccountToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OnEditAccount();
        }

        private void listViewAccounts_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            OnEditAccount();
        }

        private void OnEditAccount()
        {
            if (listViewAccounts.SelectedItems.Count > 0)
            {
                ListViewItem item = listViewAccounts.SelectedItems[0];
                AccountForm f = (AccountForm)item.Tag;
                while (f.ShowDialog() == DialogResult.OK)
                {
                    string username = f.GetUsername();

                    if (string.IsNullOrEmpty(username))
                    {
                        ShowWarning("username can't be empty!");
                    }
                    else if (!IsEmailAddress(username))
                    {
                        ShowWarning("email address is not valid!");
                    }
                    else if (string.IsNullOrEmpty(f.GetPassword()))
                    {
                        ShowWarning("password can't be empty!");
                    }
                    else if (string.IsNullOrEmpty(f.GetNickname()))
                    {
                        ShowWarning("nickname can't be empty!");
                    }
                    else if (string.IsNullOrEmpty(f.GetServer()))
                    {
                        ShowWarning("server can't be empty!");
                    }
                    else
                    {
                        item.SubItems.Clear();
                        item.Text = f.GetUsername();
                        StringBuilder b = new StringBuilder();
                        foreach (char c in f.GetPassword())
                        {
                            b.Append("*");
                        }
                        item.SubItems.Add(b.ToString());
                        item.SubItems.Add(f.GetNickname());
                        item.SubItems.Add(f.GetServer());
                        item.SubItems.Add(f.GetPort().ToString());
                        item.SubItems.Add(f.SslEnabled().ToString());
                        item.Tag = f;//为方便， 先这样
                        listViewAccounts.Update();
                        //add success.
                        break;
                    }
                }
            }
        }

        private string m_accounts = "accounts.txt";

        private string Encrypt(string text)
        {
            return Convert.ToBase64String(Encoding.Unicode.GetBytes(text));
        }

        private string Decrypt(string text)
        {
            return Encoding.Unicode.GetString(Convert.FromBase64String(text));
        }

        private void SaveAccounts()
        {
            StringBuilder sb = new StringBuilder();

            foreach (ListViewItem item in listViewAccounts.Items)
            {
                AccountForm f = (AccountForm)item.Tag;
                sb.AppendFormat("{0},{1},{2},{3},{4},{5}\n",
                    f.GetUsername(), Encrypt(f.GetPassword()), f.GetNickname(), f.GetServer(), f.GetPort(), f.SslEnabled());
            }

            File.WriteAllText(m_accounts, sb.ToString());
        }

        private void LoadAccounts()
        {
            if (File.Exists(m_accounts))
            {
                string[] lines = File.ReadAllLines(m_accounts);

                foreach (string line in lines)
                {
                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }

                    string[] parts = line.Split(',');
                    parts[1] = Decrypt(parts[1]);

                    AccountForm f = new AccountForm(parts);

                    ListViewItem item = new ListViewItem();
                    item.Text = f.GetUsername();
                    StringBuilder b = new StringBuilder();
                    foreach (char c in f.GetPassword())
                    {
                        b.Append("*");
                    }
                    item.SubItems.Add(b.ToString());
                    item.SubItems.Add(f.GetNickname());
                    item.SubItems.Add(f.GetServer());
                    item.SubItems.Add(f.GetPort().ToString());
                    item.SubItems.Add(f.SslEnabled().ToString());
                    item.Tag = f;//为方便， 先这样
                    listViewAccounts.Items.Add(item);
                }
            }
        }

        class TagedItem
        {
            public TagedItem(ListViewItem item)
            {
                this.item = item;
            }

            public ListViewItem item;

            public override string ToString()
            {
                return item.Text;
            }
        }

        private void comboBoxAccounts_DropDown(object sender, EventArgs e)
        {
            foreach (ListViewItem item in listViewAccounts.Items)
            {
                comboBoxAccounts.Items.Add(new TagedItem(item));
            }
        }
    }
}