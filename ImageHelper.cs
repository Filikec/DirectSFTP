using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using ImageMagick;

namespace DirectSFTP
{
    public static class ImageHelper
    {
        public static int THUMBNAIL_SIZE = 64;
        private static HashSet<string> extensions = new()
        {
            ".ase",
            ".art",
            ".bmp",
            ".blp",
            ".cd5",
            ".cit",
            ".cpt",
            ".cr2",
            ".cut",
            ".dds",
            ".dib",
            ".djvu",
            ".egt",
            ".exif",
            ".gif",
            ".gpl",
            ".grf",
            ".icns",
            ".ico",
            ".iff",
            ".jng",
            ".jpeg",
            ".jpg",
            ".jfif",
            ".jp2",
            ".jps",
            ".lbm",
            ".max",
            ".miff",
            ".mng",
            ".msp",
            ".nef",
            ".nitf",
            ".ota",
            ".pbm",
            ".pc1",
            ".pc2",
            ".pc3",
            ".pcf",
            ".pcx",
            ".pdn",
            ".pgm",
            ".PI1",
            ".PI2",
            ".PI3",
            ".pict",
            ".pct",
            ".pnm",
            ".pns",
            ".ppm",
            ".psb",
            ".psd",
            ".pdd",
            ".psp",
            ".px",
            ".pxm",
            ".pxr",
            ".qfx",
            ".raw",
            ".rle",
            ".sct",
            ".sgi",
            ".rgb",
            ".int",
            ".bw",
            ".tga",
            ".tiff",
            ".tif",
            ".vtf",
            ".xbm",
            ".xcf",
            ".xpm",
            ".3dv",
            ".amf",
            ".ai",
            ".awg",
            ".cgm",
            ".cdr",
            ".cmx",
            ".dxf",
            ".e2d",
            ".egt",
            ".eps",
            ".fs",
            ".gbr",
            ".odg",
            ".svg",
            ".stl",
            ".vrml",
            ".x3d",
            ".sxd",
            ".v2d",
            ".vnd",
            ".wmf",
            ".emf",
            ".art",
            ".xar",
            ".png",
            ".webp",
            ".jxr",
            ".hdp",
            ".wdp",
            ".cur",
            ".ecw",
            ".iff",
            ".lbm",
            ".liff",
            ".nrrd",
            ".pam",
            ".pcx",
            ".pgf",
            ".sgi",
            ".rgb",
            ".rgba",
            ".bw",
            ".int",
            ".inta",
            ".sid",
            ".ras",
            ".sun",
            ".tga",
            ".heic",
            ".heif"
        };
        public static bool IsImage(string fileName) {
            return extensions.Contains(Path.GetExtension(fileName).ToLower());
        }

        public static string CreateThumbnailFile(string sourceImg)
        {
            string output = Path.Join(FileSystem.CacheDirectory, "DirectSFTP");
            output = Path.Join(output,Path.GetFileName(sourceImg));

            Debug.WriteLine("Creating thumbnail for " + sourceImg + " and saving into " + output);
            using (var image = new MagickImage(sourceImg))
            {
                image.Resize(new MagickGeometry(THUMBNAIL_SIZE, THUMBNAIL_SIZE)
                {
                    IgnoreAspectRatio = false
                });

                // Save the resized image
                image.Write(output);
            }
            
            return output;
        }
    }
}
