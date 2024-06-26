﻿using CommunityToolkit.Maui.Storage;


namespace DirectSFTP;

public partial class MainPage : ContentPage
{

    public List<Tuple<string, Entry>> Settings;
    public MainPage()
    {
        InitializeComponent();

        Settings = new()
        {
            new("Host",host),
            new("Password",password),
            new("Port",port),
            new("Username",username)
        };

        foreach (var item in Settings)
        {
            var pref = Preferences.Default.Get(item.Item1, "");
            if (pref != "")
            {
                item.Item2.Text = pref;
            }
        }
        Loaded += (a, b) =>
        {
            Application.Current.UserAppTheme = AppTheme.Dark;
        };
        downloadFolderLabel.Text = SFTP.GetDownloadFolder();
    }

    public async void OnConnect(object sender, EventArgs ars)
    {

        

        var isConnected = Task.Run(() =>
        {
            try
            {
                bool connected = SFTP.GetInstance().Connect(host.Text, int.Parse(port.Text), username.Text, password.Text);
                return connected;
            }catch (Exception)
            {
                return false;
            }
            
        });

        await DisplayAlert("Alert", "Connecting...", "OK");
        var res = await isConnected;
        

        if (Preferences.Default.Get("DownloadFolder","") == "")
        {
            await DisplayAlert("Alert", "You haven't selected a download folder", "OK");
        }
        else if (res)
        {
            Dispatcher.Dispatch(() => Navigation.PushAsync(new ConnectPage()));
        }
        else
        {
            await DisplayAlert("Alert", "Couldn't connect", "OK");
        }


    }
    public async void OnSave(object sender, EventArgs ars)
    {
        foreach (var item in Settings)
        {
            Preferences.Default.Set(item.Item1, item.Item2.Text);
        }
        await DisplayAlert("Result", "Saved", "OK");
    }
    public  void OnChooseDownloadLocation(object sender, EventArgs ars)
    {

#if ANDROID
        
        var intent = new Android.Content.Intent(Android.Content.Intent.ActionOpenDocumentTree);
        Microsoft.Maui.ApplicationModel.Platform.CurrentActivity.StartActivityForResult(intent,70);
#else
        Task.Run(async () =>
        {
            CancellationToken token = new();
            var res = await FolderPicker.PickAsync(token);
            if (res.IsSuccessful)
            {
                Preferences.Default.Set("DownloadFolder", res.Folder.Path);
            }
            Dispatcher.Dispatch(()=> downloadFolderLabel.Text = SFTP.GetDownloadFolder());

        });
#endif
    }

}

