namespace WallpaperCycler
{
    /// <summary>
    /// Public contract for the main wallpaper controller.
    /// Used by RapidPanelForm (and any other consumer) to drive actions
    /// without depending on MainForm internals or reflection.
    /// </summary>
    public interface IWallpaperController
    {
        void GoToPrevious();
        void GoToNext();
        void DeleteCurrent();
        void ShowCurrentInExplorer();
        void ViewCurrentLocation();

        bool CanGoPrevious { get; }
        bool HasCurrentPhoto { get; }
        bool HasGpsLocation { get; }
    }
}
