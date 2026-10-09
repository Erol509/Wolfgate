using System.IO;
using Content.Shared._WF.CustomMarkings;
using Robust.Client.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.CustomMarkings;

/// <summary>Reads marking art from PNG: sheets the server sends, and files a player imports.</summary>
public static class CustomMarkingPng
{
    /// <summary>Largest file the importer will try to read.</summary>
    public const int MaxFileBytes = 256 * 1024;

    /// <summary>Art from a PNG of a whole sheet or of one facing; null for anything else.</summary>
    public static CustomMarkingArt? Read(byte[] png)
    {
        if (png.Length > MaxFileBytes)
            return null;

        try
        {
            using var image = Image.Load<Rgba32>(new MemoryStream(png, false));
            return CustomMarkingArt.FromImage(image.Width, image.Height, image.GetPixelSpan());
        }
        catch (Exception)
        {
            // Not an image ImageSharp can read.
            return null;
        }
    }
}
