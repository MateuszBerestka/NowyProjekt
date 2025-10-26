namespace AIMusicSorter
{
    partial class Form1
    {
        /// <summary>
        /// Wymagana zmienna projektanta.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Wyczyść wszystkie używane zasoby.
        /// </summary>
        /// <param name="disposing">prawda, jeżeli zarządzane zasoby powinny zostać zlikwidowane; Fałsz w przeciwnym wypadku.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Kod generowany przez Projektanta formularzy systemu Windows

        /// <summary>
        /// Metoda wymagana do obsługi projektanta — nie należy modyfikować
        /// jej zawartości w edytorze kodu.
        /// </summary>
        private void InitializeComponent()
        {
            this.dgvTracks = new System.Windows.Forms.DataGridView();
            this.btnFetchPlaylist = new System.Windows.Forms.Button();
            this.pieChart1 = new LiveCharts.WinForms.PieChart();
            this.panelChart = new System.Windows.Forms.Panel();
            ((System.ComponentModel.ISupportInitialize)(this.dgvTracks)).BeginInit();
            this.panelChart.SuspendLayout();
            this.SuspendLayout();
            // 
            // dgvTracks
            // 
            this.dgvTracks.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvTracks.Location = new System.Drawing.Point(464, 12);
            this.dgvTracks.Name = "dgvTracks";
            this.dgvTracks.Size = new System.Drawing.Size(328, 208);
            this.dgvTracks.TabIndex = 0;
            // 
            // btnFetchPlaylist
            // 
            this.btnFetchPlaylist.Location = new System.Drawing.Point(121, 226);
            this.btnFetchPlaylist.Name = "btnFetchPlaylist";
            this.btnFetchPlaylist.Size = new System.Drawing.Size(108, 59);
            this.btnFetchPlaylist.TabIndex = 1;
            this.btnFetchPlaylist.Text = "button1";
            this.btnFetchPlaylist.UseVisualStyleBackColor = true;
            // 
            // pieChart1
            // 
            this.pieChart1.Location = new System.Drawing.Point(3, 3);
            this.pieChart1.Name = "pieChart1";
            this.pieChart1.Size = new System.Drawing.Size(121, 97);
            this.pieChart1.TabIndex = 2;
            this.pieChart1.Text = "pieChart1";
            // 
            // panelChart
            // 
            this.panelChart.Controls.Add(this.pieChart1);
            this.panelChart.Location = new System.Drawing.Point(400, 226);
            this.panelChart.Name = "panelChart";
            this.panelChart.Size = new System.Drawing.Size(416, 151);
            this.panelChart.TabIndex = 3;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(920, 450);
            this.Controls.Add(this.panelChart);
            this.Controls.Add(this.btnFetchPlaylist);
            this.Controls.Add(this.dgvTracks);
            this.Name = "Form1";
            this.Text = "Form1";
            ((System.ComponentModel.ISupportInitialize)(this.dgvTracks)).EndInit();
            this.panelChart.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.DataGridView dgvTracks;
        private System.Windows.Forms.Button btnFetchPlaylist;
        private LiveCharts.WinForms.PieChart pieChart1;
        private System.Windows.Forms.Panel panelChart;
    }
}

