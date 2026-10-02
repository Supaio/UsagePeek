using System;
using System.Drawing;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class PetSizeDialog : DpiAwareForm
    {
        private readonly NumericUpDown sizeInput;

        public PetSizeDialog(int initialPercent)
        {
            Text = "自定义桌宠大小";
            ClientSize = new Size(320, 176);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(9, 14, 22);
            ForeColor = Color.FromArgb(233, 239, 247);
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;

            Label titleLabel = new Label();
            titleLabel.AutoSize = false;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.Font = new Font("Microsoft YaHei UI", 11f,
                FontStyle.Bold);
            titleLabel.ForeColor = ForeColor;
            titleLabel.Location = new Point(22, 18);
            titleLabel.Size = new Size(276, 28);
            titleLabel.Text = "桌宠缩放比例";

            Label hintLabel = new Label();
            hintLabel.AutoSize = false;
            hintLabel.BackColor = Color.Transparent;
            hintLabel.Font = new Font("Microsoft YaHei UI", 7.8f,
                FontStyle.Regular);
            hintLabel.ForeColor = Color.FromArgb(128, 148, 173);
            hintLabel.Location = new Point(22, 48);
            hintLabel.Size = new Size(276, 22);
            hintLabel.Text = "可设置 50%–200%，多显示器缩放仍会自动适配";

            sizeInput = new NumericUpDown();
            sizeInput.Minimum = DisplayModePreference.MinimumPetScalePercent;
            sizeInput.Maximum = DisplayModePreference.MaximumPetScalePercent;
            sizeInput.Increment = 5;
            sizeInput.Value = Math.Max((int)sizeInput.Minimum,
                Math.Min((int)sizeInput.Maximum, initialPercent));
            sizeInput.TextAlign = HorizontalAlignment.Center;
            sizeInput.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            sizeInput.BackColor = Color.FromArgb(18, 27, 39);
            sizeInput.ForeColor = Color.FromArgb(233, 239, 247);
            sizeInput.Location = new Point(22, 80);
            sizeInput.Size = new Size(112, 32);

            Label percentLabel = new Label();
            percentLabel.AutoSize = false;
            percentLabel.BackColor = Color.Transparent;
            percentLabel.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            percentLabel.ForeColor = Color.FromArgb(45, 212, 191);
            percentLabel.Location = new Point(140, 82);
            percentLabel.Size = new Size(34, 28);
            percentLabel.Text = "%";

            Button cancelButton = CreateButton("取消", false);
            cancelButton.Location = new Point(126, 128);
            cancelButton.DialogResult = DialogResult.Cancel;

            Button confirmButton = CreateButton("应用", true);
            confirmButton.Location = new Point(218, 128);
            confirmButton.DialogResult = DialogResult.OK;

            Controls.Add(titleLabel);
            Controls.Add(hintLabel);
            Controls.Add(sizeInput);
            Controls.Add(percentLabel);
            Controls.Add(cancelButton);
            Controls.Add(confirmButton);

            AcceptButton = confirmButton;
            CancelButton = cancelButton;
            InitializeDpiLayout();
        }

        public int SelectedPercent
        {
            get { return Decimal.ToInt32(sizeInput.Value); }
        }

        public void PlaceNearCursor()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            PrepareForScreen(screen);
            Rectangle area = screen.WorkingArea;
            Location = new Point(
                area.Left + Math.Max(8, (area.Width - Width) / 2),
                area.Top + Math.Max(8, (area.Height - Height) / 2));
        }

        private static Button CreateButton(string text, bool accent)
        {
            Button button = new Button();
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = accent
                ? Color.FromArgb(45, 212, 191)
                : Color.FromArgb(62, 79, 100);
            button.BackColor = accent
                ? Color.FromArgb(14, 104, 96)
                : Color.FromArgb(18, 27, 39);
            button.ForeColor = Color.FromArgb(233, 239, 247);
            button.Font = new Font("Microsoft YaHei UI", 8.2f,
                FontStyle.Bold);
            button.Size = new Size(80, 32);
            button.Text = text;
            button.UseVisualStyleBackColor = false;
            return button;
        }
    }
}
