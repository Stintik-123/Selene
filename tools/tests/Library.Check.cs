using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Selene;
class LibraryCheck
{
    static int checks;
    static void Assert(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
    static T Child<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var c=VisualTreeHelper.GetChild(parent,i);var found=c as T ?? Child<T>(c);if(found!=null)return found;}return null;
    }
    static void Wait(int ms){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(s,e)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    [STAThread]static int Main(string[] args)
    {
        string runtime=args[0];
        var temp=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"temp");Directory.CreateDirectory(temp);
        Environment.SetEnvironmentVariable("TEMP",temp);Environment.SetEnvironmentVariable("TMP",temp);
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{var name=new AssemblyName(e.Name).Name;foreach(var suffix in new[]{".dll",".exe"}){var path=Path.Combine(runtime,name+suffix);if(File.Exists(path))return Assembly.LoadFrom(path);}return null;};
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        Features.Init(new SeleneSettings(),()=>{});Features.Set("sounds",false);Features.Set("collectionglide",false);
        var panelType=Assembly.LoadFrom(Path.Combine(runtime,"Playnite.FullscreenApp.exe")).GetType("Playnite.FullscreenApp.Controls.FullscreenTilePanel",true);
        var factory=new FrameworkElementFactory(panelType);
        foreach(var pair in new[]{new object[]{"ColumnsProperty",7},new object[]{"RowsProperty",1},new object[]{"UseHorizontalLayoutProperty",true},new object[]{"SmoothScrollEnabledProperty",true}})
            factory.SetValue((DependencyProperty)panelType.GetField((string)pair[0]).GetValue(null),pair[1]);
        var ratioType=panelType.GetProperty("ItemAspectRatio").PropertyType;
        var ratioConstructor=ratioType.GetConstructors().First(c=>c.GetParameters().Length==2);
        var parameters=ratioConstructor.GetParameters();var ratio=ratioConstructor.Invoke(new[]{Convert.ChangeType(600,parameters[0].ParameterType),Convert.ChangeType(900,parameters[1].ParameterType)});
        factory.SetValue((DependencyProperty)panelType.GetField("ItemAspectRatioProperty").GetValue(null),ratio);
        var list=new ListBox{Name="PART_ListGameItems",Width=1920,Height=400,ItemsPanel=new ItemsPanelTemplate(factory)};
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Hidden);ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Disabled);ScrollViewer.SetCanContentScroll(list,true);
        using(var input=File.OpenRead(args[1])){var resources=(ResourceDictionary)XamlReader.Load(input);list.ItemContainerStyle=(Style)resources["ListGameItemStyle"];}
        for(int i=0;i<367;i++)list.Items.Add("Game "+i);
        var library=new Grid{Name="SeleneLibrary"};library.Children.Add(list);library.Children.Add(new Grid{Name="SeleneLibraryTrailerHost"});
        var window=new Window{Content=library,Width=1920,Height=450,Left=-32000,Top=-32000,Opacity=0,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None};
        try
        {
            window.Show();Wait(200);LibraryStage.Attach(window);Wait(100);
            var panel=Child<VirtualizingPanel>(list);Assert(panel!=null && panel.GetType()==panelType,"Real Playnite fullscreen panel was instantiated");
            var smooth=(DependencyProperty)panelType.GetField("SmoothScrollEnabledProperty").GetValue(null);
            Assert((bool)panel.GetValue(smooth),"Native scrolling is preserved when Selene glide is off");
            var scroll=(IScrollInfo)panel;
            int layouts=0;panel.LayoutUpdated+=(s,e)=>layouts++;
            scroll.SetHorizontalOffset(0);Wait(150);layouts=0;scroll.LineRight();Wait(240);int before=layouts;
            Features.Set("collectionglide",true);LibraryStage.ApplyNavigationSettings();
            Assert(!(bool)panel.GetValue(smooth),"Selene spring replaces the native per-frame layout animation");
            scroll.SetHorizontalOffset(0);Wait(150);layouts=0;scroll.LineRight();Wait(240);int after=layouts;
            Assert(after<before,"One logical move uses fewer layout passes than native animated scrolling");
            Assert(scroll.HorizontalOffset>0,"The logical move still advances the row");
            var item=list.ItemContainerGenerator.ContainerFromIndex(list.SelectedIndex>=0?list.SelectedIndex:1) as DependencyObject;
            Assert(item!=null && Child<Grid>(item).CacheMode is BitmapCache,"Real cover template caches the focus face and ring");
            Features.Set("collectionglide",false);LibraryStage.ApplyNavigationSettings();Assert((bool)panel.GetValue(smooth),"Turning off Selene glide restores the native setting");
            Console.WriteLine("Passed "+checks+" real Playnite panel checks. Layout passes for one move: "+before+" native animated, "+after+" with Selene logical step.");return 0;
        }
        finally{window.Close();}
    }
}
