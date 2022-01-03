using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CarrotQuant.Net.Model.Utility
{
    public static class MahAppsDialogUtility
    {
        public static async void ShowExceptionMessage(object context, IDialogCoordinator dialogCoordinator, Exception ex)
        {
            //MessageBox.Show(ex.ToString(), $"{ex.GetType().Name}:\"{ex.Message}\".");
            await dialogCoordinator.ShowMessageAsync(context,
                title: $"{ex.GetType().Name}:\"{ex.Message}\".",
                message: ex.ToString(),
                settings: new MetroDialogSettings()
                {
                    MaximumBodyHeight = 300,
                    DialogButtonFontSize = 18D,
                    DialogMessageFontSize = 12D
                });
        }
    }
}
