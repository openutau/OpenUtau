using Avalonia.Input;

namespace OpenUtau.App {
    static class ViewConstants {
        public const double TickWidthMax = 256.0 / 480.0;
        public const double TickWidthMin = 4.0 / 480.0;
        public const double TickWidthDefault = 24.0 / 480.0;
        public const double MinTicklineWidth = 12.0;

        public const double TrackHeightMax = 147;
        public const double TrackHeightMin = 42;
        public const double TrackHeightDefault = 105;
        public const double TrackHeightDelta = 21;

        public const double PianoRollTickWidthMax = 640.0 / 480.0;
        public const double PianoRollTickWidthMin = 4.0 / 480.0;
        public const double PianoRollTickWidthDefault = 128.0 / 480.0;
        public const double PianoRollTickWidthShowDetails = 64.0 / 480.0;
        public const double PianoRollMinTicklineWidth = 12.0;

        public const double PianoRollMinHeight = 24;

        public const double NoteHeightMax = 128;
        public const double NoteHeightMin = 8;
        public const double NoteHeightDefault = 22;

        public const int MaxTone = 12 * 11;

        public static readonly Cursor cursorCross = new Cursor(StandardCursorType.Cross);
        public static readonly Cursor cursorHand = new Cursor(StandardCursorType.Hand);
        public static readonly Cursor cursorNo = new Cursor(StandardCursorType.No);
        public static readonly Cursor cursorSizeAll = new Cursor(StandardCursorType.SizeAll);
        public static readonly Cursor cursorSizeNS = new Cursor(StandardCursorType.SizeNorthSouth);
        public static readonly Cursor cursorSizeWE = new Cursor(StandardCursorType.SizeWestEast);

        public const int PosMarkerHightlighZIndex = -100;

        public const int ResizeMargin = 8;

        public const int MinTrackCount = 8;
        public const int MinQuarterCount = 256;
        public const int SpareTrackCount = 4;
        public const int SpareQuarterCount = 16;

        public const double TickMinDisplayWidth = 6;
        public const double NoteMinDisplayWidth = 2;

        public const int PartRectangleZIndex = 100;
        public const int PartThumbnailZIndex = 200;
        public const int PartElementZIndex = 200;

        public const int ExpressionHiddenZIndex = 0;
        public const int ExpressionVisibleZIndex = 200;
        public const int ExpressionShadowZIndex = 100;

        public const double ExpHeightMin = 132;
        public const double ExpHeightMax = 600;
    }
}
