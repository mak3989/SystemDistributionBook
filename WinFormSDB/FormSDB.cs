using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinFormSDB.WcfServiceSDB;

namespace WinFormSDB
{
    public partial class formSDB : Form
    {
        ServiceSDBClient serviceSDB;
        string Language;
        public formSDB()
        {
            InitializeComponent();

            //задание начальных значений
            cmbSearch.SelectedIndex = 0;
            cmbLanguage.SelectedIndex = 0;
            SearchСhange();
            //создание объекта-клиента службы wcf
            serviceSDB = new ServiceSDBClient();

            //событие закрытия формы
            this.FormClosed += (sender, e) => serviceSDB.Close();
            //событие выбора комбобокса "Поиск по"
            cmbSearch.SelectedIndexChanged += (sender, e) => SearchСhange();
            //событие нажатия кнопки "Поиск"
            btnSearch.Click += Search;
            //событие нажатия кнопки "Информация о клиенте" в таблице "Статистика по клиентам"
            btnInfoClientCS.Click += SearchClient;
            //событие нажатия кнопки "Информация о книгах"
            btnInfoBooks.Click += SearchBook;
            //событие нажатия кнопки "Информация о клиенте" в таблице "Книги"
            btnInfoClientB.Click += SearchClient;
            //событие нажатия кнопки "Статистика клиента"
            btnClientStatistics.Click += SearchStatisticsClient;
        }
        /// <summary>
        /// Поиск статистики клиента
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchStatisticsClient(object sender, EventArgs e)
        {
            DataSet ds = new DataSet();
            string address = dgvBooks.Rows[dgvBooks.CurrentCell.RowIndex].Cells["AddressClient"].Value.ToString();
            ds = serviceSDB.GetClientStatistics("", Language, address);
            dgvClientsStatistics.DataSource = ds.Tables[0];
            ClientStatisticsTable();
        }
        /// <summary>
        /// Поиск книг
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchBook(object sender, EventArgs e)
        {
            DataSet ds = new DataSet();
            string address = dgvClientsStatistics.Rows[dgvClientsStatistics.CurrentCell.RowIndex].Cells["Address"].Value.ToString();
            ds = serviceSDB.GetBook(0, Language, "", address);
            dgvBooks.DataSource = ds.Tables[0];
            BookTable();
        }
        /// <summary>
        /// Преобразование таблицы книг
        /// </summary>
        private void BookTable()
        {
            dgvBooks.Columns[0].Width = 200;
            dgvBooks.Columns[0].HeaderText = "Адресс клиента";
            dgvBooks.Columns[1].HeaderText = "Язык";
            dgvBooks.Columns[2].HeaderText = "Название книги";
            dgvBooks.Columns[3].Width = 70;
            dgvBooks.Columns[3].HeaderText = "Страниц";
            dgvBooks.Columns[4].HeaderText = "Дата получения";
            dgvBooks.Columns[5].HeaderText = "Дата прочтения";
            dgvBooks.Columns[6].Visible = false;
        }
        /// <summary>
        /// Поиск информации по клиенту
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchClient(object sender, EventArgs e)
        {
            DataSet ds = new DataSet();
            string address;
            //проверка с какой кнопки вызван метод
            if (sender == btnInfoClientCS)
                address = dgvClientsStatistics.Rows[dgvClientsStatistics.CurrentCell.RowIndex].Cells["Address"].Value.ToString();
            else
                address = dgvBooks.Rows[dgvBooks.CurrentCell.RowIndex].Cells["AddressClient"].Value.ToString();
            ds = serviceSDB.GetClientInfo(address);
            dgvClientInfo.DataSource = ds.Tables[0];
            dgvClientInfo.Columns[0].Width = 200;
            dgvClientInfo.Columns[0].HeaderText = "Адресс клиента";
            dgvClientInfo.Columns[1].HeaderText = "Фамилия";
            dgvClientInfo.Columns[2].HeaderText = "Имя";
            dgvClientInfo.Columns[3].HeaderText = "Чтение страниц в день";
            dgvClientInfo.Columns[3].Width = 115;
            dgvClientInfo.Columns[4].HeaderText = "Количество дней чтения";
            dgvClientInfo.Columns[5].HeaderText = "Количество дней отдыха";
            dgvClientInfo.Columns[6].HeaderText = "Дата регистрации";
            dgvClientInfo.Columns[7].HeaderText = "Статус подписки";

            dgvLabelLanguage.DataSource = ds.Tables[1];
            dgvLabelLanguage.Columns[0].Visible = false;
            dgvLabelLanguage.Columns[1].HeaderText = "Язык";
            dgvLabelLanguage.Columns[2].Width = 120;
            dgvLabelLanguage.Columns[2].HeaderText = "Уровень чтения (от 1 до 10)";
        }
        /// <summary>
        /// Поиск в зависимости отвыброного поиска по клиентам или книгам
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Search(object sender, EventArgs e)
        {
            ///очистка таблиц
            dgvClientsStatistics.DataSource = null;
            dgvLabelLanguage.DataSource = null;
            dgvClientInfo.DataSource = null;
            dgvBooks.DataSource = null;
            DataSet ds = new DataSet();
            //поиск статистики по клиентам
            if (cmbSearch.SelectedIndex == 0)
            {
                btnInfoClientB.Enabled = false;
                btnClientStatistics.Enabled = false;
                ds = serviceSDB.GetClientStatistics(cmbStatus.Text, cmbLanguage.Text, txbClientBook.Text);
                dgvClientsStatistics.DataSource = ds.Tables[0];
                ClientStatisticsTable();
                Language = cmbLanguage.Text;
                btnInfoBooks.Enabled = true;
                btnInfoClientCS.Enabled = true;
            }
            //поиск книг
            else if (cmbSearch.SelectedIndex == 1)
            {
                btnInfoBooks.Enabled = false;
                btnInfoClientCS.Enabled = false;               
                ds = serviceSDB.GetBook(cmbStatus.SelectedIndex, cmbLanguage.Text, txbClientBook.Text, "");
                dgvBooks.DataSource = ds.Tables[0];
                BookTable();
                Language = cmbLanguage.Text;
                btnInfoClientB.Enabled = true;
                btnClientStatistics.Enabled = true;
            }
        }
        /// <summary>
        /// Преобразование таблицы статистики по клиентам
        /// </summary>
        private void ClientStatisticsTable()
        {
            dgvClientsStatistics.Columns[0].Width = 200;
            dgvClientsStatistics.Columns[0].HeaderText = "Адресс клиента";
            dgvClientsStatistics.Columns[1].HeaderText = "Прочитано книг";
            dgvClientsStatistics.Columns[2].HeaderText = "Прочитано страниц";
        }
        /// <summary>
        /// Изменение котролов в зависимости от поиска по клиентам или книгам
        /// </summary>
        private void SearchСhange()
        {
            if (cmbSearch.SelectedIndex == 0)
            {
                lblStatus.Text = "Статус подписки";
                lblClientBook.Text = "Адресс клиента";
                cmbStatus.Items.Clear();
                cmbStatus.Items.AddRange(new object[] {
                                    "Все",
                                    "Подписан",
                                    "Отписан"});
                cmbStatus.SelectedIndex = 0;
            }
            else if (cmbSearch.SelectedIndex == 1)
            {
                lblStatus.Text = "Статус чтения";
                lblClientBook.Text = "Название книги";
                cmbStatus.Items.Clear();
                cmbStatus.Items.AddRange(new object[] {
                                    "Все",
                                    "Прочитана",
                                    "Читается"});
                cmbStatus.SelectedIndex = 0;
            }            
        }
    }
}
