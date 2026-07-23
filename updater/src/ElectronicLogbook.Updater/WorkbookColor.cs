namespace ElectronicLogbook.Updater;

internal static class WorkbookColor
{
    public static int WithLightness(int sourceColor, double targetLightness)
    {
        var red = (sourceColor & 0xFF) / 255.0;
        var green = ((sourceColor >> 8) & 0xFF) / 255.0;
        var blue = ((sourceColor >> 16) & 0xFF) / 255.0;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));

        if (Math.Abs(maximum - minimum) < double.Epsilon)
        {
            var grey = (int)Math.Round(targetLightness * 255);
            return grey + (grey << 8) + (grey << 16);
        }

        var lightness = (maximum + minimum) / 2.0;
        var saturation = lightness > 0.5
            ? (maximum - minimum) / (2.0 - maximum - minimum)
            : (maximum - minimum) / (maximum + minimum);

        double hue;
        if (Math.Abs(maximum - red) < double.Epsilon)
        {
            hue = (green - blue) / (maximum - minimum);
            if (green < blue)
            {
                hue += 6.0;
            }
        }
        else if (Math.Abs(maximum - green) < double.Epsilon)
        {
            hue = (blue - red) / (maximum - minimum) + 2.0;
        }
        else
        {
            hue = (red - green) / (maximum - minimum) + 4.0;
        }

        hue /= 6.0;
        var second = targetLightness < 0.5
            ? targetLightness * (1.0 + saturation)
            : targetLightness + saturation - (targetLightness * saturation);
        var first = (2.0 * targetLightness) - second;
        var outRed = (int)Math.Round(255 * HueChannel(first, second, hue + (1.0 / 3.0)));
        var outGreen = (int)Math.Round(255 * HueChannel(first, second, hue));
        var outBlue = (int)Math.Round(255 * HueChannel(first, second, hue - (1.0 / 3.0)));
        return outRed + (outGreen << 8) + (outBlue << 16);
    }

    public static int ContrastingTextColor(int backgroundColor)
    {
        var red = backgroundColor & 0xFF;
        var green = (backgroundColor >> 8) & 0xFF;
        var blue = (backgroundColor >> 16) & 0xFF;
        var brightness = ((red * 299) + (green * 587) + (blue * 114)) / 1000.0;
        return brightness >= 150 ? 0 : 0xFFFFFF;
    }

    private static double HueChannel(double first, double second, double hue)
    {
        if (hue < 0)
        {
            hue += 1.0;
        }
        if (hue > 1)
        {
            hue -= 1.0;
        }

        if (hue < 1.0 / 6.0)
        {
            return first + ((second - first) * 6.0 * hue);
        }
        if (hue < 1.0 / 2.0)
        {
            return second;
        }
        if (hue < 2.0 / 3.0)
        {
            return first + ((second - first) * ((2.0 / 3.0) - hue) * 6.0);
        }
        return first;
    }
}
