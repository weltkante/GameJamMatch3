namespace Match3
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.display = new Match3.DisplayControl();
            this.renderTimer = new System.Windows.Forms.Timer(this.components);
            this.SuspendLayout();
            // 
            // display
            // 
            this.display.Location = new System.Drawing.Point(62, 48);
            this.display.Name = "display";
            this.display.Size = new System.Drawing.Size(320, 320);
            this.display.TabIndex = 0;
            // 
            // renderTimer
            // 
            this.renderTimer.Enabled = true;
            this.renderTimer.Tick += new System.EventHandler(this.renderTimer_Tick);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(524, 489);
            this.Controls.Add(this.display);
            this.Name = "MainForm";
            this.Text = "Match 3";
            this.ResumeLayout(false);

        }

        #endregion

        private DisplayControl display;
        private System.Windows.Forms.Timer renderTimer;
    }
}

