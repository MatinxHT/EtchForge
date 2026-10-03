using EtchForge.Models;

namespace EtchForge.Services;

public interface IImageInspector
{
    Task<ImageInfo> InspectAsync(string path, IProgress<double>? progress, CancellationToken token);
}

public sealed class ImageInspectorAdapter : IImageInspector
{
    public Task<ImageInfo> InspectAsync(string path, IProgress<double>? progress, CancellationToken token) =>
        ImageInspector.InspectAsync(path, progress, token);
}

public interface IImageConverter
{
    bool IsQemuAvailable { get; }
    Task<IReadOnlyList<string>> ConvertAsync(ImageInfo image, OutputFormat format,
        string outputDirectory, IProgress<double>? progress, CancellationToken token);
}

public sealed class ImageConverterAdapter : IImageConverter
{
    public bool IsQemuAvailable => ConversionService.FindQemuImg() is not null;

    public Task<IReadOnlyList<string>> ConvertAsync(ImageInfo image, OutputFormat format,
        string outputDirectory, IProgress<double>? progress, CancellationToken token) =>
        ConversionService.ConvertAsync(image, format, outputDirectory, progress, token);
}
