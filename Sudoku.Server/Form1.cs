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

namespace Sudoku.Server
{
    public partial class Form1 : Form
    {
        private readonly GameApplicationServices _gameServices;

        public Form1()
        {
            InitializeComponent();
            _gameServices = new GameApplicationServices();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _gameServices.Dispose();
            base.OnFormClosed(e);
        }
    }
}
