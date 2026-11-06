using Android.App;
using Avalonia.Android;

namespace OpenUtau.Mobile;

[Activity(
    Label = "OpenUtau Mobile",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity {
}

[Application]
public class MobileAndroidApplication : AvaloniaAndroidApplication<MobileApp> {
    public MobileAndroidApplication(IntPtr javaReference, Android.Runtime.JniHandleOwnership transfer)
        : base(javaReference, transfer) {
    }
}
