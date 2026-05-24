namespace OpenUtau.Core.Util {
    public class WindowSize {
        public double Width { get; set; } = 1200;
        public double Height { get; set; } = 650;
        public int? PositionX { get; set; }
        public int? PositionY { get; set; }
        public int State { get; set; }

        public bool TryGetPosition(out int x, out int y) {
            x = PositionX ?? 0;
            y = PositionY ?? 0;
            return PositionX != null && PositionY != null;
        }

        public void Set(double width, double height, int posX, int posY, int state) {
            if (state == 0) { // When WindowState is Normal
                Width = width;
                Height = height;
            }
            PositionX = posX;
            PositionY = posY;
            switch (state) {
                case 1: // Minimized
                    State = 0; // Launch as normal next time
                    break;
                case 2: // Maximized
                    State = 2;
                    break;
                case 3: // FullScreen
                    State = 2; // Convert to Maximized so the taskbar doesn't hide
                    break;
                default: // Normal
                    State = 0;
                    break;
            }
        }
    }
}
