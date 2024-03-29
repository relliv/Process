﻿using GalaSoft.MvvmLight;
using Process.Data;
using Process.Helpers;
using Process.Models.AppSetting;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using static Process.DI.DI;

namespace Process.ViewModel.App
{
    public class AppSettingsViewModel : ViewModelBase
    {
        public AppSettingsViewModel()
        {
            SaveSettingCommand = new RelayParameterizedCommand(SaveSetting);

            using var db = new AppDbContext();
            var Settings = db.AppSettings.OrderByDescending(x => x.IsEditable)
            .ToObservableCollection();

            UserSettings = Settings.Where(x => x.IsEditable).ToObservableCollection();
            AppSettings = Settings.Where(x => x.IsEditable == false).ToObservableCollection();
        }

        public ICommand SaveSettingCommand { get; set; }

        public ObservableCollection<AppSetting> UserSettings { get; set; }
        public ObservableCollection<AppSetting> AppSettings { get; set; }

        public void SaveSetting(object sender)
        {
            var appSetting = (sender as TextBox).DataContext as AppSetting;

            using var db = new AppDbContext();
            db.AppSettings.Update(appSetting);
            db.SaveChanges();

            ViewModelApplication.AppSettings.LoadSettings();
        }
    }
}