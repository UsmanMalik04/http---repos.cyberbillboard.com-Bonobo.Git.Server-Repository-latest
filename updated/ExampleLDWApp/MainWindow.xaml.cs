using ExampleLDWApp.Properties;
using MahApps.Metro.Controls.Dialogs;
using MetroRadiance.Platform;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;

using static LDW.ImageProcessing;
using static LDW.PCSetup;
using static LDW.ThemeColours;
using static LDW.ThemeHelper;
using static LDW.WallpaperWrapper;
using LDW;
using LDW.Models;
using LDWApp.Ext;
using ExampleLDWApp;

namespace LDWApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : INotifyPropertyChanged
    {
        private string serverConfig = Settings.Default.ServerDomain;
        private XmlNamespaceManager nsmgr;
        private XmlNode _selectedTheme;
        private string selectedTheme;
        private ProgressDialogController pdlg;
        private FeedCollection _fc;
        public PhotoList _pl;
        private List<FileInfo> _currentBackgrounds;
        private ShowImage temp_Window;
        public bool check = false;

        public MainWindow()
        {
            InitializeComponent();

            this.Closed += MainWindow_Closed;
            var nt = new NameTable();
            nsmgr = new XmlNamespaceManager(nt);
            nsmgr.AddNamespace("s", "http://schemas.datacontract.org/2004/07/LDWSVC.Models");
            SetDataContext();

        }

        private void MainWindow_MouseDown(object sender, EventArgs e)
        {

        }



        void MainWindow_Closed(object sender, EventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void SetDataContext()
        {
            DataContext = this;
            CurrentTheme = GetCurrentThemeName();
            var wc = new WebClient();
            var dataUrl = string.Format("{0}/api/{1}/{2}/{3}", serverConfig, "rw", Settings.Default.ClientID.ToString(), Settings.Default.ClientHash);
            var req = (HttpWebRequest)WebRequest.Create(dataUrl);
            req.Accept = "application/xml";
            var resp = (HttpWebResponse)req.GetResponse();
            var xmlDoc = new XmlDocument();
            xmlDoc.Load(resp.GetResponseStream());
            XmlDataProvider provider = (XmlDataProvider)FindResource("themes");
            provider.Document = xmlDoc;
            provider.Refresh();
            var selectedTheme = CurrentTheme;
            if (!string.IsNullOrEmpty(selectedTheme))
                SelectedTheme = provider.Document.SelectSingleNode("/s:ArrayOfSubscribableViewModel/s:SubscribableViewModel[s:Name = '" + selectedTheme + "']", nsmgr);
            _pl = new PhotoList();
            _fc = new FeedCollection();
            _currentBackgrounds = GetCurrentWallpaper().Select(w => new FileInfo(w)).ToList();

        }

        private async void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var selected = SourceChoices.SelectedItem as XmlElement;
            var title = selected.SelectSingleNode("s:Name", nsmgr).InnerText;
            string myurl;
            var themeFileName = CleanForFileName(title);
            if (!ThemeFileExists(title))
            {
                pdlg = await this.ShowProgressAsync("Please wait", "Downloading Theme");
                pdlg.SetIndeterminate();
                await Task.Delay(100);
                ThemeDirCreated += OnDirCreated;
                NewTheme(title);
                var iconImg = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.FullName + "\\AppIcon.png";
                var fi = new FileInfo(iconImg);
                if (!fi.Exists)
                    ExtractIcon(iconImg);
                SetThemeIcon(iconImg);
                SetWallpaperStyle(IsWindows7OrNewer ? WallpaperStyle.Fit : WallpaperStyle.Crop);
                myurl = string.Format("{0}/api/feed/{1}/{2}?ts={3}",
                                        Settings.Default.ServerDomain,
                                        Settings.Default.ClientID,
                                        selected.SelectSingleNode("s:Id", nsmgr).InnerText,
                                        DateTime.Now.Ticks);
            }
            else
            {
                SwitchTheme(title);
                var picsFolder = GetEnclosureFolder();
                if (string.IsNullOrEmpty(picsFolder)) // theme not rss
                {
                    Console.WriteLine("Theme not RSS");
                }
                else if (LinkFolder.IsChecked.HasValue && LinkFolder.IsChecked.Value)
                {
                    if (!IsAFolderLinked(title))
                        LinkEnclosureFolder(GetEnclosureFolder(), title);
                }
                else if (IsAFolderLinked(title))
                    RemoveLinkedFolder(title);
                myurl = GetRSSUrl();
            }
            ThemeSwitchEvent += OnThemeChanged;
            SetRSSUrl(myurl);
            SetShuffle(DoShuffle.IsChecked.HasValue && DoShuffle.IsChecked.Value);
            SetSlideInterval(Convert.ToInt32(intervalMins.Value), 0);
            SetAutoColor(AutoColor.IsChecked.HasValue && AutoColor.IsChecked.Value);
            SetEnableDarkTheme(DarkThemeEnable.IsChecked.HasValue && DarkThemeEnable.IsChecked.Value);
            SetThemeSyncSetting(SyncTheme.IsChecked.HasValue && SyncTheme.IsChecked.Value);
            ApplyTheme();
            this.UpdateLayout();
            SourceChoices.Focus();
            Photos = new PhotoList();
            CurrentBackgrounds = GetCurrentWallpaper().Select(w => new FileInfo(w)).ToList();
        }

        private void OnDirCreated(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (LinkFolder.IsChecked.HasValue && LinkFolder.IsChecked.Value)
                    LinkEnclosureFolder(ThemeFolderName, CleanForFileName(CurrentTheme, false));
            });
        }
        private void OnThemeChanged(object sender, EventArgs e)
        {
            Dispatcher.Invoke(async () =>
            {
                await pdlg.CloseAsync();
            });
        }
        private void SourceChoices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = SourceChoices.SelectedItem as XmlElement;
            FocusedTheme = selected.SelectSingleNode("s:Name", nsmgr).InnerText;
            SetControls();
        }
        private void SourceChoices_LayoutUpdated(object sender, EventArgs e)
        {
            var theme = CurrentTheme;
            XmlElement myTheme = null;
            if (!string.IsNullOrEmpty(theme) && theme != selectedTheme)
                myTheme = SourceChoices.Items.Cast<XmlElement>().FirstOrDefault(xe => xe.SelectSingleNode("s:Name", nsmgr).InnerText == theme);

            if (myTheme != null)
            {
                SourceChoices.SelectedItem = myTheme;
                var millis = GetSlideInterval();
                SetControls();
                UpdateLayout();
                SourceChoices.Focus();
                selectedTheme = theme;
                SelectedTheme = myTheme;
            }
        }

        private void SetControls()
        {
            btnApply.IsEnabled = false;
            intervalMins.IsEnabled = false;
            DoShuffle.IsEnabled = false;
            LinkFolder.IsEnabled = false;
            ResetBtn.IsEnabled = false;
            //Download.IsEnabled = false;
            if (SourceChoices.SelectedItem != null)
            {
                var millis = GetSlideInterval();
                intervalMins.Value = TimeSpan.FromMilliseconds(millis).Minutes;
                DoShuffle.IsChecked = GetShuffleSetting();
                AutoColor.IsChecked = GetAutoColor();
                DarkThemeEnable.IsChecked = GetEnableDarkTheme();
                SyncTheme.IsChecked = GetThemeSyncSetting();
                intervalMins.IsEnabled = true;
                btnApply.IsEnabled = true;
                DoShuffle.IsEnabled = true;
                LinkFolder.IsEnabled = true;
                string uri = SelectedTheme?.SelectSingleNode("s:WebUrl", nsmgr).InnerText;
                try
                {
                    //if (GetFeedByUrl(uri) is Feed sel) EmbossTitle.IsChecked = Directory.EnumerateFiles(sel.LocalEnclosurePath).Take(10).Any(f => !IsNotWatermarked(new FileInfo(f)));
                }
                catch { }

            }
            if (FocusedFeed != null)
            {
                ResetBtn.IsEnabled = true;
                //Download.IsEnabled = true;
            }
        }

        private void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentTheme == FocusedTheme) SwitchToAeroTheme();
            var selected = SourceChoices.SelectedItem as XmlElement;
            ResetFeeds(new Dictionary<string, string> { { selected.SelectSingleNode("s:WebUrl", nsmgr).InnerText, selected.SelectSingleNode("s:Name", nsmgr).InnerText } });
        }

        private void ResetAllBtn_Click(object sender, RoutedEventArgs e)
        {
            SwitchToAeroTheme();
            var uris = new Dictionary<string, string>();
            foreach (XmlElement el in SourceChoices.Items)
            {
                if (!uris.ContainsKey(el.SelectSingleNode("s:WebUrl", nsmgr).InnerText))
                    uris.Add(el.SelectSingleNode("s:WebUrl", nsmgr).InnerText, el.SelectSingleNode("s:Name", nsmgr).InnerText);
            }
            ResetFeeds(uris);
        }

        private void ResetFeed_Click(object sender, RoutedEventArgs e)
        {
            var select = MetroDataGrid.SelectedItem as Feed;
            ResetFeeds(new Dictionary<string, string> { { select.Link ?? select.Name, select.Name } });
        }

        private void Download_Click(object sender, RoutedEventArgs e)
        {
            DownloadFeed();
        }


        void ListBox_MouseLeftButtonDown(object sender, RoutedEventArgs e)
        {

        }

        private void DarkThemeEnable_Checked(object sender, RoutedEventArgs e)
        {
            var ph = new MaterialDesignThemes.Wpf.PaletteHelper();
            if (DarkThemeEnable?.IsChecked == true)
            {
                ph.SetLightDark(true);
            }
            else
            {
                ph.SetLightDark(false);
            }
        }

        public FeedCollection Feeds
        {
            get
            {
                return _fc;

            }
            set
            {
                _fc = value;
                OnPropertyChanged("Feeds");
            }
        }
        public PhotoList Photos
        {
            get
            {
                return _pl;
            }
            set
            {
                _pl = value;
                OnPropertyChanged("Photos");
            }
        }
        public List<FileInfo> CurrentBackgrounds
        {
            get
            {

                return _currentBackgrounds;

            }
            set
            {
                _currentBackgrounds = value;
                OnPropertyChanged("CurrentBackgrounds");
            }
        }

        public XmlNode SelectedTheme
        {
            get { return _selectedTheme; }
            set
            {
                _selectedTheme = value;
                if (value == null) return;
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(_selectedTheme.OuterXml);
                XmlDataProvider provider = (XmlDataProvider)FindResource("current");
                provider.Document = xmlDoc;
                provider.Refresh();
            }
        }

        private void AutoColor_Checked(object sender, RoutedEventArgs e)
        {
            var ph = new MaterialDesignThemes.Wpf.PaletteHelper();
            if (AutoColor.IsChecked == true)
            {
                WindowsTheme.Accent.Changed += Accent_Changed;
                string colorName;
                FindColour(WindowsTheme.Accent.Current, out colorName);

                ph.ReplacePrimaryColor(ColorMapping[colorName]);
            }
            else
            {
                WindowsTheme.Accent.Changed -= Accent_Changed;
                ph.ReplacePrimaryColor("Amber");
            }

        }

        private void Accent_Changed(object sender, Color e)
        {

            var ph = new MaterialDesignThemes.Wpf.PaletteHelper();
            string colorName;
            FindColour(WindowsTheme.Accent.Current, out colorName);
            try
            {
                ph.ReplacePrimaryColor(ColorMapping[colorName]);
            }
            catch { }
            CurrentBackgrounds = GetCurrentWallpaper().Select(w => new FileInfo(w)).ToList();
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            Feeds = new FeedCollection();
        }

        #region INotifyPropertyChanged Members
        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        #endregion

        private void PhotoStatusBtn_Click(object sender, RoutedEventArgs e)
        {
            CheckBox o = sender as CheckBox;
            var s = FindResource("PhotoStatus");
        }

        public ICommand PhotoStatusCommand { get; set; }

        private void DockPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var panel = (DockPanel)sender;

            var maxWidth = panel.ActualWidth - CurrentDesc.ActualWidth;
            //CurrentWallpapers.Width = maxWidth;
        }

        private void CurrentWallpapers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CurrentWallpapers.ScrollIntoView(CurrentWallpapers.SelectedItem);
            CurrentWallpapers.ScrollToCenterOfView(CurrentWallpapers.SelectedItem);

        }

        private void ScrollViewer_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollViewer scv = (ScrollViewer)sender;
            scv.ScrollToHorizontalOffset(scv.HorizontalOffset - Math.Floor(e.Delta / 6d));
            e.Handled = true;
        }

        private async void EmbossTitle_Checked(object sender, EventArgs e)
        {
            ToggleButton o = (ToggleButton)sender;
            if (o.IsChecked.HasValue && o.IsChecked.Value)
            {
                pdlg = await this.ShowProgressAsync("Please wait", "Writing Titles");
                pdlg.SetIndeterminate();
                var result = Task.Run(() => WaterMarkDirectory(CurrentFeed.LocalEnclosurePath));
                await result;
            }
            else
            {
                pdlg = await this.ShowProgressAsync("Please wait", "Redownloading Images");
                pdlg.SetIndeterminate();
                foreach (FeedItem item in GetCurrentFeedImageSet())
                {
                    if (!IsNotWatermarked(new FileInfo(item.Enclosure.LocalPath))) continue;
                    try
                    {
                        item.Enclosure.RemoveFile();
                    }
                    catch { }
                    item.Enclosure.AsyncDownload();
                }
                Thread.Sleep(2000);
            }
            await pdlg.CloseAsync();
            var wallpaper = GetWallpaper();
            for (uint i = 0; i < wallpaper.GetMonitorDevicePathCount();)
            {
                try
                {
                    string path = wallpaper.GetMonitorDevicePathAt(i);
                    wallpaper.AdvanceSlideshow(null, DesktopSlideshowDirection.Forward);
                    //wallpaper.AdvanceSlideshow(null, DesktopSlideshowDirection.Backward);
                }
                catch (Exception ex)
                {
                    var x = ex.Message;
                }
            }
        }


        int scrollIndex = 0;
        private void onClickNextBtn(object sender, RoutedEventArgs e)
        {

            if (scrollIndex < Carousel.Items.Count && scrollIndex > -1)
            {
                try
                {
                    if (temp_Window != null)
                    {
                        string filename = "";
                        var source = temp_Window.img.Source;
                        string hreflink = source.ToString();
                        Uri uri = new Uri(hreflink);
                        if (uri.IsFile)
                        {
                             filename = System.IO.Path.GetFileName(uri.LocalPath);
                        }
                        var length = _pl.Count;
                        for (int i = 0; i < length; i++)
                        {
                            var listSource = _pl[i].FileInfo.Name;
                            if (listSource == filename)
                            {
                                var NextImage= _pl[i+1].Source;
                                var strImgPath = NextImage.Replace(@"\\", @"\");

                                

                                imageClaz obj = new imageClaz();
                                obj.imgArt = new BitmapImage(new Uri(strImgPath, UriKind.Absolute));
                                temp_Window.img.Source = obj.imgArt;
                            }
                        }
                    }
                    else
                    {
                        Carousel.ScrollIntoView(Carousel.Items[++scrollIndex]);
                    }
                }
                catch (Exception ex)
                {
                }


            }
        }
        public class imageClaz
        {
            public BitmapImage imgArt { get; set; }
        }
        private void onClickPreviousBtn(object sender, RoutedEventArgs e)
        {
            try
            {



                int backIndex = scrollIndex - 8;
                if (backIndex > -1)
                {
                    if (temp_Window != null)
                    {
                        string filename = "";
                        var source = temp_Window.img.Source;
                        string hreflink = source.ToString();
                        Uri uri = new Uri(hreflink);
                        if (uri.IsFile)
                        {
                            filename = System.IO.Path.GetFileName(uri.LocalPath);
                        }
                        var length = _pl.Count;
                        for (int i = 0; i < length; i++)
                        {
                            var listSource = _pl[i].FileInfo.Name;
                            if (listSource == filename)
                            {
                                var NextImage = _pl[i - 1].Source;
                                var strImgPath = NextImage.Replace(@"\\", @"\");



                                imageClaz obj = new imageClaz();
                                obj.imgArt = new BitmapImage(new Uri(strImgPath, UriKind.Absolute));
                                temp_Window.img.Source = obj.imgArt;
                            }
                        }
                    }
                    else
                    {
                        Carousel.ScrollIntoView(Carousel.Items[backIndex]);
                        if (scrollIndex > 0)
                            scrollIndex--;
                    }
                }
            }
            catch (Exception ex)
            {
            }

        }
        int itemsInView = 8;
        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            //logic to find out items in view for Carousal
            //set itemsInView

            scrollIndex = itemsInView;
        }

        private void Expander_Expanded(object sender, RoutedEventArgs e)
        {
            foreach (var exp in ExpanderStack.Children)
            {
                if (exp != sender)
                    ((Expander)exp).IsExpanded = false;
            }
        }

        //private void bla()
        //{
        //  string foo = Carousel.Items.GetItemAt(0).ToString();
        //   MessageBox.Show(foo);
        //}

        private void aPicture_MouseDown(object sender, MouseButtonEventArgs e)
        {

            if (temp_Window != null)
            {
                temp_Window.Close();
                temp_Window = null;
            }
            var clickedImage = ((LDW.Models.Photo)((System.Windows.FrameworkElement)sender).DataContext).Source;

            temp_Window = new ShowImage();
            temp_Window.img.Source = new BitmapImage(new Uri(clickedImage));
            temp_Window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            temp_Window.Topmost = false;
            check = true;
            temp_Window.Show();
            temp_Window.Owner = this;
            //MessageBox.Show("Index: " + clickedImage.ToString());
        }

        private void MetroWindow_MouseDown(object sender, MouseButtonEventArgs e)
        {

            if (temp_Window != null && check != true)
            {
                temp_Window.Close();
                temp_Window = null;
                check = false;
            }
            else
            {
                check = false;
            }


        }

    }

    public class ImgConverter : IValueConverter
    {
        BitmapImage bi;
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
            {
                var val = value as FileInfo;

                var height = parameter == null ? 200 : int.Parse(parameter.ToString());
                var format = GetImageFormat(File.ReadAllBytes(val.FullName)) ?? System.Drawing.Imaging.ImageFormat.Jpeg;
                var pic = ResizeImage(val, 450, height);
                if (pic == null) return null;
                MemoryStream ms = new MemoryStream();
                pic.Save(ms, format);
                ms.Position = 0;
                bi = new BitmapImage();
                bi.BeginInit();
                // Image thumbnailImage = new Image();
                // thumbnailImage.Width = 100;
                // thumbnailImage.Height = 100;
                bi.DecodePixelWidth = 400;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                // bi.UriSource = new Uri(val.ToString());
                // bi.DecodePixelWidth = 450;

                //  bi.DecodePixelHeight = 450;
                bi.StreamSource = ms;
                bi.EndInit();
                // BitmapImage bi = new BitmapImage(new Uri(val.FullName), new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.CacheIfAvailable));
                //bi.CacheOption = BitmapCacheOption.OnLoad;

                return bi;
            }
            else
            {
                return null;
            }

        }

        public BitmapImage bla()
        {
            return bi;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }



}
