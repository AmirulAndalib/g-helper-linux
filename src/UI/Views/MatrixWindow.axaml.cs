using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using GHelper.Linux.Helpers;
using SkiaSharp;

namespace GHelper.Linux.UI.Views;

/// <summary>
/// Settings window for AnimeMatrix picture configuration.
/// Loads an image/GIF with SkiaSharp, applies zoom/contrast/gamma,
/// and renders a greyscale preview simulating the LED matrix.
/// </summary>
public partial class MatrixWindow : Window
{
    private bool _suppressEvents = true;
    private SKBitmap? _sourceBitmap;

    public MatrixWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            _suppressEvents = true;

            trackZoom.Value = AppConfig.Get("matrix_zoom", 100);
            trackContrast.Value = AppConfig.Get("matrix_contrast", 100);
            trackGamma.Value = AppConfig.Get("matrix_gamma", 0);
            comboScaling.SelectedIndex = Math.Clamp(AppConfig.Get("matrix_quality", 0), 0, 5);
            comboRotation.SelectedIndex = Math.Clamp(AppConfig.Get("matrix_rotation", 0), 0, 1);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _suppressEvents = false;
                LoadSourceImage();
                RenderPreview();
            }, Avalonia.Threading.DispatcherPriority.Background);
        };

        Closed += (_, _) =>
        {
            _sourceBitmap?.Dispose();
            _sourceBitmap = null;
        };
    }

    // Slider/combo handlers

    private void TrackZoom_ValueChanged(object? sender,
        Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        AppConfig.Set("matrix_zoom", (int)e.NewValue);
        RenderPreview();
        ApplyToDevice();
    }

    private void TrackContrast_ValueChanged(object? sender,
        Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        AppConfig.Set("matrix_contrast", (int)e.NewValue);
        RenderPreview();
        ApplyToDevice();
    }

    private void TrackGamma_ValueChanged(object? sender,
        Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        AppConfig.Set("matrix_gamma", (int)e.NewValue);
        RenderPreview();
        ApplyToDevice();
    }

    private void ComboScaling_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        AppConfig.Set("matrix_quality", comboScaling.SelectedIndex);
        RenderPreview();
        ApplyToDevice();
    }

    private void ComboRotation_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
            return;
        AppConfig.Set("matrix_rotation", comboRotation.SelectedIndex);
        RenderPreview();
        ApplyToDevice();
    }

    // Button handlers

    private async void ButtonPicture_Click(object? sender, RoutedEventArgs e)
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Select Image or GIF",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"]
                }
            ]
        };

        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0)
        {
            string path = files[0].Path.LocalPath;
            AppConfig.Set("matrix_picture", path);
            Logger.WriteLine($"MatrixWindow: selected {path}");
            LoadSourceImage();
            RenderPreview();
            ApplyToDevice();
        }
    }

    private void ButtonReset_Click(object? sender, RoutedEventArgs e)
    {
        _suppressEvents = true;

        trackZoom.Value = 100;
        trackContrast.Value = 100;
        trackGamma.Value = 0;
        comboScaling.SelectedIndex = 0;
        comboRotation.SelectedIndex = 0;

        AppConfig.Set("matrix_zoom", 100);
        AppConfig.Set("matrix_contrast", 100);
        AppConfig.Set("matrix_gamma", 0);
        AppConfig.Set("matrix_quality", 0);
        AppConfig.Set("matrix_rotation", 0);

        _suppressEvents = false;

        RenderPreview();
        ApplyToDevice();
    }

    // Image loading

    private void LoadSourceImage()
    {
        _sourceBitmap?.Dispose();
        _sourceBitmap = null;

        string? path = AppConfig.GetString("matrix_picture");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            labelImageInfo.Text = "";
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            var info = codec.Info;
            _sourceBitmap = SKBitmap.Decode(codec);

            int frameCount = codec.FrameCount;
            string name = Path.GetFileName(path);
            string frameInfo = frameCount > 1 ? $", {frameCount} frames" : "";
            labelImageInfo.Text = $"{name} ({info.Width}x{info.Height}{frameInfo})";
        }
        catch (Exception ex)
        {
            labelImageInfo.Text = $"Error: {ex.Message}";
            Logger.WriteLine($"MatrixWindow: load failed: {ex.Message}");
        }
    }

    // Preview rendering

    private void RenderPreview()
    {
        if (_sourceBitmap == null)
        {
            previewImage.Source = null;
            return;
        }

        try
        {
            int zoom = (int)trackZoom.Value;
            int contrast = (int)trackContrast.Value;
            int gamma = (int)trackGamma.Value;

            // Target size for the AnimeMatrix LED grid preview.
            // The actual LED grid is ~34x61 (GA402) but we render at a higher
            // resolution so the preview looks good in the 200px-tall border.
            int previewW = 340;
            int previewH = 200;

            float scale = zoom / 100f;
            int srcW = _sourceBitmap.Width;
            int srcH = _sourceBitmap.Height;

            // Fit image into preview, then apply zoom
            float fitScale = Math.Min((float)previewW / srcW, (float)previewH / srcH);
            float totalScale = fitScale * scale;

            int drawW = (int)(srcW * totalScale);
            int drawH = (int)(srcH * totalScale);
            int drawX = (previewW - drawW) / 2;
            int drawY = (previewH - drawH) / 2;

            using var output = new SKBitmap(previewW, previewH, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(output);
            canvas.Clear(new SKColor(10, 10, 26)); // match border background

            // Scaling quality from combo
            var sampling = comboScaling.SelectedIndex switch
            {
                1 => new SKSamplingOptions(SKFilterMode.Linear),
                2 => new SKSamplingOptions(SKCubicResampler.Mitchell),
                3 => new SKSamplingOptions(SKFilterMode.Linear),            // bilinear
                4 => new SKSamplingOptions(SKCubicResampler.CatmullRom),    // bicubic
                5 => new SKSamplingOptions(SKFilterMode.Nearest),           // nearest
                _ => new SKSamplingOptions(SKFilterMode.Linear),
            };

            using var paint = new SKPaint
            {
                IsAntialias = comboScaling.SelectedIndex != 5,
            };

            // Apply contrast + gamma via color matrix filter.
            // Contrast scales RGB around 0.5 midpoint. Gamma shifts brightness.
            float c = contrast / 100f;
            float g = gamma / 255f;
            float translate = 0.5f * (1f - c) + g;

            // Convert to greyscale and apply contrast/gamma in one pass.
            // Greyscale weights: R=0.299, G=0.587, B=0.114
            float rw = 0.299f * c;
            float gw = 0.587f * c;
            float bw = 0.114f * c;

            var colorMatrix = new float[]
            {
                rw, gw, bw, 0, translate,
                rw, gw, bw, 0, translate,
                rw, gw, bw, 0, translate,
                0,  0,  0,  1, 0,
            };
            paint.ColorFilter = SKColorFilter.CreateColorMatrix(colorMatrix);

            var destRect = new SKRect(drawX, drawY, drawX + drawW, drawY + drawH);
            var srcRect = new SKRect(0, 0, srcW, srcH);
            using var srcImage = SKImage.FromBitmap(_sourceBitmap);
            canvas.DrawImage(srcImage, srcRect, destRect, sampling, paint);

            // Convert SKBitmap to Avalonia Bitmap via PNG encode
            using var image = SKImage.FromBitmap(output);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            using var ms = new MemoryStream(data.ToArray());
            previewImage.Source = new Bitmap(ms);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"MatrixWindow: render preview failed: {ex.Message}");
        }
    }

    // Send to AnimeMatrix device (if connected)

    private void ApplyToDevice()
    {
        string? path = AppConfig.GetString("matrix_picture");
        if (string.IsNullOrEmpty(path))
            return;

        Task.Run(() =>
        {
            try
            { App.AnimeMatrix?.SetMatrixPicture(path); }
            catch (Exception ex)
            {
                Logger.WriteLine($"MatrixWindow: device apply failed: {ex.Message}");
            }
        });
    }
}
