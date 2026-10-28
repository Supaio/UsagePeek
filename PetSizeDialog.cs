using System;
using System.Drawing;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class PetSizeDialog : DpiAwareForm
    {
        private readonly ModernNumberControl sizeInput;

        public PetSizeDialog(int initialPercent)
        {
            Text = "自定义桌宠大小";
            ClientSize = new Size(320, 176);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = UiColors.Background;
            ForeColor = UiColors.Text;
            AutoScaleMode = AutoScaleMode.None;
            AutoScroll = true;

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
            hintLabel.ForeColor = UiColors.Muted;
            hintLabel.Location = new Point(22, 48);
            hintLabel.Size = new Size(276, 22);
            hintLabel.Text = "可设置 50%–200%，多显示器缩放仍会自动适配";

            sizeInput = new ModernNumberControl();
            sizeInput.Minimum = DisplayModePreference.MinimumPetScalePercent;
            sizeInput.Maximum = DisplayModePreference.MaximumPetScalePercent;
            sizeInput.Increment = 5;
            sizeInput.Value = Math.Max((int)sizeInput.Minimum,
                Math.Min((int)sizeInput.Maximum, initialPercent));
            sizeInput.TextAlign = HorizontalAlignment.Center;
            sizeInput.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            sizeInput.BackColor = UiColors.Surface;
            sizeInput.ForeColor = UiColors.Text;
            sizeInput.Location = new Point(22, 80);
            sizeInput.Size = new Size(152, 32);

            Label percentLabel = new Label();
            percentLabel.AutoSize = false;
            percentLabel.BackColor = Color.Transparent;
            percentLabel.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            percentLabel.ForeColor = UiColors.Secondary;
            percentLabel.Location = new Point(140, 82);
            percentLabel.Size = new Size(34, 28);
            percentLabel.Text = string.Empty;

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
            Button button = new RoundedButton();
            UiDrawing.StyleButton(button, accent);
            button.Font = new Font("Microsoft YaHei UI", 8.2f,
                FontStyle.Regular);
            button.Size = new Size(80, 32);
            button.Text = text;
            return button;
        }
    }
}
