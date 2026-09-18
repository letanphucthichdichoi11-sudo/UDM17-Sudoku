using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sudoku.Server.Game;
using Sudoku.Server.Network;

namespace Sudoku.Server
{
    public partial class Form1 : Form
    {
        private readonly GameApplicationServices _gameServices;
        private readonly Network.Server _server;

        public Form1()
        {
            InitializeComponent();
            _gameServices = new GameApplicationServices();
            _server = new Network.Server(_gameServices, 5000);
            StartServerAsync();
        }

        private async void StartServerAsync()
        {
            try { await _server.StartAsync(); }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Server error");
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _server.Dispose();
            _gameServices.Dispose();
            base.OnFormClosed(e);
        }
    }
}
