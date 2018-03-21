namespace LDWApp
{
    internal class OwnedWindow
    {
        public OwnedWindow()
        {
        }

        public MainWindow Owner { get; internal set; }
    }
}