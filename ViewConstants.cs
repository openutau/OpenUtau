using Avalonia.Input;

namespace OpenUtau.App {
    static class ViewConstants {
        public const double TickWidthMax = 256.0 / 480.0;
        public const double TickWidthMin = 4.0 / 480.0;
        public const double TickWidthDefault = 24.0 / 480.0;
        public const double MinTicklineWidth = 12.0;

        public const double TrackHeightMax = 147;
        public const double TrackHeightMin = 42;
        public const double TrackHeightDefault = 84;
        public const double TrackHeightDelta = 21;

        public const double TrackHeaderBaseWidth = 300;
        public const double TrackHeaderChipStep = 26;
        public const double TrackSettingsInlineTop = 29;
        public const double TrackMetaInsetForSettings = 28;

        public const double PianoRollTickWidthMax = 640.0 / 480.0;
        public const double PianoRollTickWidthMin = 4.0 / 480.0;
        public const double PianoRollTickWidthDefault = 128.0 / 480.0;
        public const double PianoRollTickWidthShowDetails = 64.0 / 480.0;
        public const double PianoRollMinTicklineWidth = 12.0;

        public const double PianoRollMinHeight = 24;
        public const double TracksPanelMinHeight = 24;

        public const double NoteHeightMax = 128;
        public const double NoteHeightMin = 8;
        public const double NoteHeightDefault = 22;

        public const int MaxTone = 12 * 11;

        public static readonly Cursor cursorCross = new Cursor(StandardCursorType.Cross);
        public static readonly Cursor cursorHand = new Cursor(StandardCursorType.Hand);
        public static readonly Cursor cursorHandGrab = new Cursor(StandardCursorType.SizeWestEast);
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

        public const double ExpPanelHeightDefault = 155;
        public const double ExpHeightMin = 132;
        public const double ExpHeightMax = 600;

        public const double PhonemePanelHeightDefault = 58;
        public const double PhonemePanelHeightMin = 44;
        public const double PhonemePanelHeightMax = 250;
        public const double PhonemePanelResizeHandleHeight = 8;
        public const double PhonemeTagStripHeight = 20;  // DiffSinger: space for tag above bars

        // Embedded (classic) phoneme strip inside the piano roll — top to bottom.
        // Alias strip height is fixed; row Y positions are inside it. Normal row may extend slightly into the gap below.
        public const double PhonemeAliasRaisedTextY = 0;
        public const double PhonemeAliasNormalTextY = 17;
        public const double PhonemeAliasChipHeight = 15;
        public const double PhonemeAliasStripHeight = 27;
        public const double PhonemeAliasEnvelopeGap = 10;
        public const double PhonemeClassicEnvelopeHeight = 24;
        public const double PhonemeEmbeddedBackgroundBottomOpacity = 0.75;
        public const double PhonemeEmbeddedHeight = PhonemeAliasStripHeight + PhonemeAliasEnvelopeGap + PhonemeClassicEnvelopeHeight;

        /// <summary>Classic phoneme strip envelope band inside the piano roll.</summary>
        public static (double barY, double barHeight) GetClassicPhonemeEnvelopeLayout() {
            return (PhonemeAliasStripHeight + PhonemeAliasEnvelopeGap, PhonemeClassicEnvelopeHeight);
        }
    }
}
