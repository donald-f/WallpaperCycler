using GeoTimeZone;
using TimeZoneConverter;

namespace WallpaperCycler
{
    public static class PhotoMetadataHelper
    {
        public static DateTime? GetLocalizedDateTaken(string imagePath)
        {
            if (!File.Exists(imagePath))
                return null;

            try
            {
                using var img = new System.Drawing.Bitmap(imagePath);
                var props = img.PropertyItems;

                // EXIF DateTimeOriginal = 0x9003
                var dateProp = Array.Find(props, p => p.Id == 0x9003);
                if (dateProp == null)
                    return null;

                // EXIF stores dates as "YYYY:MM:DD HH:MM:SS" — fix the date separator
                string raw = System.Text.Encoding.ASCII.GetString(dateProp.Value!).Trim('\0');
                string normalized = raw.Length >= 10
                    ? raw[..10].Replace(":", "-") + raw[10..]
                    : raw;

                if (!DateTime.TryParse(normalized, out var dateTaken))
                    return null;

                // Attempt GPS timezone localisation
                var latProp    = Array.Find(props, p => p.Id == 0x0002); // GPSLatitude
                var lonProp    = Array.Find(props, p => p.Id == 0x0004); // GPSLongitude
                var latRefProp = Array.Find(props, p => p.Id == 0x0001); // GPSLatitudeRef
                var lonRefProp = Array.Find(props, p => p.Id == 0x0003); // GPSLongitudeRef

                if (latProp != null && lonProp != null && latRefProp != null && lonRefProp != null)
                {
                    double lat = DecodeRationalTriplet(latProp.Value!);
                    double lon = DecodeRationalTriplet(lonProp.Value!);

                    string latRef = System.Text.Encoding.ASCII.GetString(latRefProp.Value!).Trim('\0');
                    string lonRef = System.Text.Encoding.ASCII.GetString(lonRefProp.Value!).Trim('\0');

                    if (latRef == "S") lat = -lat;
                    if (lonRef == "W") lon = -lon;

                    string ianaZone    = TimeZoneLookup.GetTimeZone(lat, lon).Result;
                    string windowsZone = TZConvert.IanaToWindows(ianaZone);
                    var    tz          = TimeZoneInfo.FindSystemTimeZoneById(windowsZone);

                    return TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.SpecifyKind(dateTaken, DateTimeKind.Utc), tz);
                }

                // No GPS — return date as-is
                return dateTaken;
            }
            catch (Exception ex)
            {
                Logger.Log($"PhotoMetadataHelper.GetLocalizedDateTaken failed for '{imagePath}': {ex.Message}");
                return null;
            }
        }

        // Each GPS coordinate is 3 rationals (degrees, minutes, seconds), each 8 bytes.
        private static double DecodeRationalTriplet(byte[] bytes)
        {
            double[] v = new double[3];
            for (int i = 0; i < 3; i++)
            {
                uint num = BitConverter.ToUInt32(bytes, i * 8);
                uint den = BitConverter.ToUInt32(bytes, i * 8 + 4);
                v[i] = den == 0 ? 0 : (double)num / den;
            }
            return v[0] + v[1] / 60.0 + v[2] / 3600.0;
        }
    }
}
