using System;
using System.IO;
using GeoTimeZone;
using TimeZoneConverter;

public static class PhotoMetadataHelper
{
    public static DateTime? GetLocalizedDateTaken(string imagePath)
    {
        if (!File.Exists(imagePath))
            return null;

        try
        {
            using (var img = new System.Drawing.Bitmap(imagePath))
            {
                // EXIF tag for DateTimeOriginal = 36867
                // GPS Latitude = 2, GPS Longitude = 4
                var propItems = img.PropertyItems;

                // get date taken
                var dateTakenItem = Array.Find(propItems, p => p.Id == 0x9003);
                if (dateTakenItem == null)
                    return null;

                string dateTakenStr = System.Text.Encoding.ASCII.GetString(dateTakenItem.Value).Trim('\0');
                dateTakenStr = dateTakenStr.Length >= 10
                    ? dateTakenStr.Substring(0, 10).Replace(":", "-") + dateTakenStr.Substring(10)
                    : dateTakenStr;
                if (!DateTime.TryParse(dateTakenStr, out var dateTaken))
                    return null;


                // get GPS info if present
                var latItem = Array.Find(propItems, p => p.Id == 0x0002);
                var lonItem = Array.Find(propItems, p => p.Id == 0x0004);
                var latRefItem = Array.Find(propItems, p => p.Id == 0x0001);
                var lonRefItem = Array.Find(propItems, p => p.Id == 0x0003);

                if (latItem != null && lonItem != null && latRefItem != null && lonRefItem != null)
                {
                    double latitude = DecodeRationalTriplet(latItem.Value);
                    double longitude = DecodeRationalTriplet(lonItem.Value);

                    string latRef = System.Text.Encoding.ASCII.GetString(latRefItem.Value).Trim('\0');
                    string lonRef = System.Text.Encoding.ASCII.GetString(lonRefItem.Value).Trim('\0');

                    if (latRef == "S") latitude = -latitude;
                    if (lonRef == "W") longitude = -longitude;

                    // Lookup timezone
                    string ianaZone = TimeZoneLookup.GetTimeZone(latitude, longitude).Result;
                    string windowsZone = TZConvert.IanaToWindows(ianaZone);

                    var tz = TimeZoneInfo.FindSystemTimeZoneById(windowsZone);
                    return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(dateTaken, DateTimeKind.Utc), tz);
                }

                return dateTaken; // fallback: return as-is if no GPS data
            }
        }
        catch
        {
            return null;
        }
    }

    private static double DecodeRationalTriplet(byte[] bytes)
    {
        // Each coordinate is stored as 3 rationals (degrees, minutes, seconds)
        double[] values = new double[3];
        for (int i = 0; i < 3; i++)
        {
            uint numerator = BitConverter.ToUInt32(bytes, i * 8);
            uint denominator = BitConverter.ToUInt32(bytes, i * 8 + 4);
            values[i] = denominator == 0 ? 0 : (double)numerator / denominator;
        }

        return values[0] + (values[1] / 60) + (values[2] / 3600);
    }
}
