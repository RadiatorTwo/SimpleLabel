using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SimpleLabel.Utilities;

public static class ImageProcessing
{
    /// <summary>
    /// Main entry point for image processing with all options
    /// </summary>
    public static WriteableBitmap ProcessImage(
        BitmapSource source,
        string algorithm = "Threshold",
        byte threshold = 128,
        bool invert = false,
        double brightness = 0,
        double contrast = 0)
    {
        // Convert source to Bgra32 format
        var convertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int width = convertedBitmap.PixelWidth;
        int height = convertedBitmap.PixelHeight;
        int stride = width * 4;

        // Read source pixels
        byte[] pixels = new byte[height * stride];
        convertedBitmap.CopyPixels(pixels, stride, 0);

        // Step 1: Apply brightness and contrast adjustments
        if (Math.Abs(brightness) > 0.01 || Math.Abs(contrast) > 0.01)
        {
            ApplyBrightnessContrast(pixels, brightness, contrast);
        }

        // Step 2: Apply monochrome algorithm
        byte[] resultPixels = algorithm switch
        {
            "FloydSteinberg" => ApplyFloydSteinberg(pixels, width, height, stride, threshold),
            "Ordered" => ApplyOrderedDithering(pixels, width, height, stride, threshold),
            "Atkinson" => ApplyAtkinson(pixels, width, height, stride, threshold),
            _ => ApplyThresholdInternal(pixels, threshold)
        };

        // Step 3: Apply invert if requested
        if (invert)
        {
            InvertPixels(resultPixels);
        }

        // Create WriteableBitmap and write result
        var writeableBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        writeableBitmap.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), resultPixels, stride, 0);

        return writeableBitmap;
    }

    /// <summary>
    /// Legacy method for backward compatibility
    /// </summary>
    public static WriteableBitmap ApplyThreshold(BitmapSource source, byte threshold)
    {
        return ProcessImage(source, "Threshold", threshold, false, 0, 0);
    }

    private static void ApplyBrightnessContrast(byte[] pixels, double brightness, double contrast)
    {
        // Normalize brightness (-100 to 100 -> -255 to 255)
        double b = brightness * 2.55;

        // Normalize contrast (-100 to 100 -> factor)
        double c = (contrast + 100.0) / 100.0;
        c = c * c; // Square for better feel

        for (int i = 0; i < pixels.Length; i += 4)
        {
            for (int j = 0; j < 3; j++) // Process B, G, R channels
            {
                // Apply contrast around midpoint (128)
                double value = pixels[i + j];
                value = ((value - 128.0) * c) + 128.0;

                // Apply brightness
                value += b;

                // Clamp to 0-255
                pixels[i + j] = (byte)Math.Clamp(value, 0, 255);
            }
        }
    }

    private static byte[] ApplyThresholdInternal(byte[] pixels, byte threshold)
    {
        byte[] result = new byte[pixels.Length];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];

            // Calculate grayscale
            byte gray = (byte)(0.299 * r + 0.587 * g + 0.114 * b);

            // Apply threshold
            byte value = (byte)(gray >= threshold ? 255 : 0);

            result[i] = value;     // B
            result[i + 1] = value; // G
            result[i + 2] = value; // R
            result[i + 3] = 255;   // A
        }

        return result;
    }

    private static byte[] ApplyFloydSteinberg(byte[] pixels, int width, int height, int stride, byte threshold)
    {
        byte[] result = new byte[pixels.Length];
        Array.Copy(pixels, result, pixels.Length);

        // Convert to grayscale first
        int[] gray = new int[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * stride + x * 4;
                int idx = y * width + x;
                byte b = result[i];
                byte g = result[i + 1];
                byte r = result[i + 2];
                gray[idx] = (int)(0.299 * r + 0.587 * g + 0.114 * b);
            }
        }

        // Floyd-Steinberg dithering
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                int oldPixel = gray[idx];
                int newPixel = oldPixel >= threshold ? 255 : 0;
                gray[idx] = newPixel;

                int error = oldPixel - newPixel;

                // Distribute error to neighboring pixels
                if (x + 1 < width)
                    gray[idx + 1] = Math.Clamp(gray[idx + 1] + (error * 7 / 16), 0, 255);

                if (y + 1 < height)
                {
                    if (x > 0)
                        gray[idx + width - 1] = Math.Clamp(gray[idx + width - 1] + (error * 3 / 16), 0, 255);

                    gray[idx + width] = Math.Clamp(gray[idx + width] + (error * 5 / 16), 0, 255);

                    if (x + 1 < width)
                        gray[idx + width + 1] = Math.Clamp(gray[idx + width + 1] + (error * 1 / 16), 0, 255);
                }
            }
        }

        // Write back to result
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * stride + x * 4;
                int idx = y * width + x;
                byte value = (byte)gray[idx];

                result[i] = value;
                result[i + 1] = value;
                result[i + 2] = value;
                result[i + 3] = 255;
            }
        }

        return result;
    }

    private static byte[] ApplyOrderedDithering(byte[] pixels, int width, int height, int stride, byte threshold)
    {
        byte[] result = new byte[pixels.Length];

        // 4x4 Bayer matrix
        int[,] bayerMatrix = new int[4, 4]
        {
            {  0,  8,  2, 10 },
            { 12,  4, 14,  6 },
            {  3, 11,  1,  9 },
            { 15,  7, 13,  5 }
        };

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * stride + x * 4;
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];

                // Calculate grayscale
                int gray = (int)(0.299 * r + 0.587 * g + 0.114 * b);

                // Apply ordered dithering
                int bayerValue = bayerMatrix[y % 4, x % 4];
                int adjustedThreshold = threshold + (bayerValue - 8) * 8;

                byte value = (byte)(gray >= adjustedThreshold ? 255 : 0);

                result[i] = value;
                result[i + 1] = value;
                result[i + 2] = value;
                result[i + 3] = 255;
            }
        }

        return result;
    }

    private static byte[] ApplyAtkinson(byte[] pixels, int width, int height, int stride, byte threshold)
    {
        byte[] result = new byte[pixels.Length];
        Array.Copy(pixels, result, pixels.Length);

        // Convert to grayscale first
        int[] gray = new int[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * stride + x * 4;
                int idx = y * width + x;
                byte b = result[i];
                byte g = result[i + 1];
                byte r = result[i + 2];
                gray[idx] = (int)(0.299 * r + 0.587 * g + 0.114 * b);
            }
        }

        // Atkinson dithering
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                int oldPixel = gray[idx];
                int newPixel = oldPixel >= threshold ? 255 : 0;
                gray[idx] = newPixel;

                int error = oldPixel - newPixel;

                // Distribute error (Atkinson distributes 6/8 of error, not 100%)
                if (x + 1 < width)
                    gray[idx + 1] = Math.Clamp(gray[idx + 1] + (error / 8), 0, 255);

                if (x + 2 < width)
                    gray[idx + 2] = Math.Clamp(gray[idx + 2] + (error / 8), 0, 255);

                if (y + 1 < height)
                {
                    if (x > 0)
                        gray[idx + width - 1] = Math.Clamp(gray[idx + width - 1] + (error / 8), 0, 255);

                    gray[idx + width] = Math.Clamp(gray[idx + width] + (error / 8), 0, 255);

                    if (x + 1 < width)
                        gray[idx + width + 1] = Math.Clamp(gray[idx + width + 1] + (error / 8), 0, 255);
                }

                if (y + 2 < height)
                {
                    gray[idx + width * 2] = Math.Clamp(gray[idx + width * 2] + (error / 8), 0, 255);
                }
            }
        }

        // Write back to result
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * stride + x * 4;
                int idx = y * width + x;
                byte value = (byte)gray[idx];

                result[i] = value;
                result[i + 1] = value;
                result[i + 2] = value;
                result[i + 3] = 255;
            }
        }

        return result;
    }

    private static void InvertPixels(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(255 - pixels[i]);         // B
            pixels[i + 1] = (byte)(255 - pixels[i + 1]); // G
            pixels[i + 2] = (byte)(255 - pixels[i + 2]); // R
            // Alpha stays the same
        }
    }
}
