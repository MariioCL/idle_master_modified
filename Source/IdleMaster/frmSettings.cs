using System;
using System.Drawing;
using System.Windows.Forms;
using IdleMaster.Properties;
using System.Threading;
using System.Text.RegularExpressions;

namespace IdleMaster
{
  public partial class frmSettings : Form
  {
    public frmSettings()
    {
      InitializeComponent();
    }

    private void btnCancel_Click(object sender, EventArgs e)
    {
      Close();
    }

    private void btnOK_Click(object sender, EventArgs e)
    {
        Settings.Default.sort = "default";

        if (cboLanguage.Text != "")
        {
            if (cboLanguage.Text != Settings.Default.language)
            {
                MessageBox.Show(localization.strings.please_restart);
            }
            Settings.Default.language = cboLanguage.Text;
        }

        Settings.Default.minToTray = chkMinToTray.Checked;
        Settings.Default.ignoreclient = chkIgnoreClientStatus.Checked;
        Settings.Default.showUsername = chkShowUsername.Checked;
        Settings.Default.NoSleep = noSleepBox.Checked;
        Settings.Default.Save();
        Close();
    }

    private void frmSettings_Load(object sender, EventArgs e)
    {
        if (Settings.Default.language != "")
        {
            cboLanguage.SelectedItem = Settings.Default.language;            
        }
        else
        {
            switch (Thread.CurrentThread.CurrentUICulture.EnglishName)
            {
                case "Chinese (Simplified, China)":
                case "Chinese (Traditional, China)":
                case "Portuguese (Brazil)":
                    cboLanguage.SelectedItem = Thread.CurrentThread.CurrentUICulture.EnglishName;
                    break;
                default:
                    cboLanguage.SelectedItem = Regex.Replace(Thread.CurrentThread.CurrentUICulture.EnglishName, @"\(.+\)", "").Trim();
                    break;
            }
        }

        // Load translation
        this.Text = localization.strings.idle_master_settings;
        grpGeneral.Text = localization.strings.general;
        grpIdlingQuantity.Text = localization.strings.idling_behavior;
        grpPriority.Text = localization.strings.idling_order;
        btnOK.Text = localization.strings.accept;
        btnCancel.Text = localization.strings.cancel;
        ttHints.SetToolTip(btnAdvanced, localization.strings.advanced_auth);
        chkMinToTray.Text = localization.strings.minimize_to_tray;
        ttHints.SetToolTip(chkMinToTray, localization.strings.minimize_to_tray);
        chkIgnoreClientStatus.Text = localization.strings.ignore_client_status;
        ttHints.SetToolTip(chkIgnoreClientStatus, localization.strings.ignore_client_status);
        chkShowUsername.Text = localization.strings.show_username;
        ttHints.SetToolTip(chkShowUsername, localization.strings.show_username);
        radOneGameOnly.Text = localization.strings.idle_individual;
        ttHints.SetToolTip(radOneGameOnly, localization.strings.idle_individual);
        radIdleDefault.Text = localization.strings.order_default;
        ttHints.SetToolTip(radIdleDefault, localization.strings.order_default);
        lblLanguage.Text = localization.strings.interface_language;
        noSleepBox.Text = localization.strings.no_sleep;
        darkThemeCheckBox.Text = localization.strings.dark_theme;

        radOneGameOnly.Checked = Settings.Default.OnlyOneGameIdle;

        if(Settings.Default.sort == "Default")
            {
                radIdleDefault.Checked = true;
            }

        if (Settings.Default.minToTray)
        {
            chkMinToTray.Checked = true;
        }

        if (Settings.Default.ignoreclient)
        {
            chkIgnoreClientStatus.Checked = true;
        }

        if (Settings.Default.showUsername)
        {
            chkShowUsername.Checked = true;
        }
        if (Settings.Default.NoSleep)
        {
            noSleepBox.Checked = true;
        }

        runtimeCustomThemeSettings(); // JN: Apply theme colors and icons
    }

    // JN: Change the colors of the form components to match the dark theme
    private void runtimeCustomThemeSettings()
    {
        // Read settings
        var customTheme = Settings.Default.customTheme;

        // Set checkboxes (Not necessary, as the checkboxes are bound to the global setting)
        //darkThemeBox.Checked = customTheme;
        
        if (customTheme)
        {
            // Custom theme colors (could be user selected, probably)
            Settings.Default.colorBgd = Color.FromArgb(38, 38, 38);
            Settings.Default.colorTxt = Color.FromArgb(196, 196, 196);
        }

        // Define colors
        Color colorBgd = customTheme ? Settings.Default.colorBgd : Settings.Default.colorBgdOriginal;
        Color colorTxt = customTheme ? Settings.Default.colorTxt : Settings.Default.colorTxtOriginal;
        
        // Define button style
        FlatStyle buttonStyle = customTheme ? FlatStyle.Flat : FlatStyle.Standard;

        // --------------------------
        // -- APPLY THEME SETTINGS --
        // --------------------------

        // Form colors
        this.BackColor = colorBgd;
        this.ForeColor = colorTxt;

        // Group title colors
        grpGeneral.ForeColor = grpIdlingQuantity.ForeColor = grpPriority.ForeColor = colorTxt;

        // Dropdown
        cboLanguage.BackColor = colorBgd;
        cboLanguage.ForeColor = colorTxt;

        // Buttons
        btnOK.FlatStyle = btnCancel.FlatStyle = btnAdvanced.FlatStyle = buttonStyle;
        btnOK.BackColor = btnCancel.BackColor = btnAdvanced.BackColor = colorBgd;
        btnOK.ForeColor = btnCancel.ForeColor = btnAdvanced.ForeColor = colorTxt;

        // Icons
        btnAdvanced.Image = Settings.Default.whiteIcons ? Resources.imgLock_w : Resources.imgLock;

        // Apply to the main frame window
        //this.Parent.Refresh();
        // Save the settings
        Settings.Default.Save();
    }

    private void btnAdvanced_Click(object sender, EventArgs e)
    {
      var frm = new frmSettingsAdvanced();
      frm.ShowDialog();
    }

    private void darkThemeBox_CheckedChanged(object sender, EventArgs e)
    {
        Settings.Default.customTheme = darkThemeCheckBox.Checked; // Save the dark theme setting
        runtimeCustomThemeSettings(); // JN: Apply the dark theme
    }
    }
}
