namespace CommonService.Application.Features.Workers.Services;

/// <summary>
/// Pure algorithm for Variance of Laplacian (VoL) image blur score calculation (decisions.md Q03, BR-06).
/// Computes variance of convolution output with 3x3 Laplacian kernel on a 640px resized grayscale pixel matrix.
/// </summary>
public static class LaplacianVarianceCalculator
{
    public const int DefaultResizeWidthPx = 640;

    /// <summary>
    /// Computes Variance of Laplacian for a 2D grayscale pixel matrix (values 0..255).
    /// </summary>
    public static double CalculateFromGrayscaleMatrix(double[,] grayscalePixels, int resizeWidthPx = DefaultResizeWidthPx)
    {
        int height = grayscalePixels.GetLength(0);
        int width = grayscalePixels.GetLength(1);

        if (width <= 0 || height <= 0)
        {
            return 0.0;
        }

        // Resize matrix if width exceeds resizeWidthPx while maintaining aspect ratio
        double[,] processedMatrix = grayscalePixels;
        if (width > resizeWidthPx)
        {
            processedMatrix = ResizeGrayscaleMatrix(grayscalePixels, resizeWidthPx);
            height = processedMatrix.GetLength(0);
            width = processedMatrix.GetLength(1);
        }

        if (width < 3 || height < 3)
        {
            return 0.0; // Cannot compute 3x3 kernel on images smaller than 3x3
        }

        // Convolve with 3x3 Laplacian kernel: [[0, 1, 0], [1, -4, 1], [0, 1, 0]]
        int interiorWidth = width - 2;
        int interiorHeight = height - 2;
        int count = interiorWidth * interiorHeight;
        double[] laplacianOutput = new double[count];

        int index = 0;
        double sum = 0.0;

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                double lapVal = processedMatrix[y - 1, x]
                              + processedMatrix[y + 1, x]
                              + processedMatrix[y, x - 1]
                              + processedMatrix[y, x + 1]
                              - 4.0 * processedMatrix[y, x];

                laplacianOutput[index++] = lapVal;
                sum += lapVal;
            }
        }

        double mean = sum / count;
        double sumSquareDiff = 0.0;

        for (int i = 0; i < count; i++)
        {
            double diff = laplacianOutput[i] - mean;
            sumSquareDiff += diff * diff;
        }

        return sumSquareDiff / count;
    }

    /// <summary>
    /// Computes Variance of Laplacian from a 3D RGB pixel array (height x width x 3).
    /// </summary>
    public static double CalculateFromRgbMatrix(byte[,,] rgbPixels, int resizeWidthPx = DefaultResizeWidthPx)
    {
        int height = rgbPixels.GetLength(0);
        int width = rgbPixels.GetLength(1);

        double[,] grayscale = new double[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte r = rgbPixels[y, x, 0];
                byte g = rgbPixels[y, x, 1];
                byte b = rgbPixels[y, x, 2];
                // NTSC formula for RGB -> Grayscale conversion
                grayscale[y, x] = 0.299 * r + 0.587 * g + 0.114 * b;
            }
        }

        return CalculateFromGrayscaleMatrix(grayscale, resizeWidthPx);
    }

    /// <summary>
    /// Resizes a 2D grayscale matrix to target width using bilinear interpolation.
    /// </summary>
    private static double[,] ResizeGrayscaleMatrix(double[,] source, int targetWidth)
    {
        int srcHeight = source.GetLength(0);
        int srcWidth = source.GetLength(1);

        double scale = (double)targetWidth / srcWidth;
        int targetHeight = Math.Max(1, (int)Math.Round(srcHeight * scale));

        double[,] target = new double[targetHeight, targetWidth];

        for (int y = 0; y < targetHeight; y++)
        {
            double srcY = y / scale;
            int y0 = (int)Math.Floor(srcY);
            int y1 = Math.Min(y0 + 1, srcHeight - 1);
            double dy = srcY - y0;

            for (int x = 0; x < targetWidth; x++)
            {
                double srcX = x / scale;
                int x0 = (int)Math.Floor(srcX);
                int x1 = Math.Min(x0 + 1, srcWidth - 1);
                double dx = srcX - x0;

                double top = source[y0, x0] * (1.0 - dx) + source[y0, x1] * dx;
                double bottom = source[y1, x0] * (1.0 - dx) + source[y1, x1] * dx;

                target[y, x] = top * (1.0 - dy) + bottom * dy;
            }
        }

        return target;
    }
}
