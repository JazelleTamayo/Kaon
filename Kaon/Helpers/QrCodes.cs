using Kaon.Data;
using Kaon.Models;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace Kaon.Helpers;

// Module 12: QR pictures and scan counts, shared by the owner's and the Kaon admin's pages
public static class QrCodes
{
    // The QR picture, made on our own server (the link is never sent to another website)
    public static byte[] Png(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(20);
    }

    public static async Task<QrStats> StatsAsync(KaonDbContext db, int storeId)
    {
        var now = DateTime.UtcNow;
        var todayUtc = DateTime.Today.ToUniversalTime();
        var scans = await db.QrScans.AsNoTracking().Where(s => s.StoreId == storeId)
            .Select(s => new { s.At, s.Source, s.SignedUpUserId }).ToListAsync();

        return new QrStats
        {
            Today = scans.Count(s => s.At >= todayUtc),
            Last7Days = scans.Count(s => s.At >= now.AddDays(-7)),
            Last30Days = scans.Count(s => s.At >= now.AddDays(-30)),
            Total = scans.Count,
            SignUps = scans.Count(s => s.SignedUpUserId != null),
            BySource = scans.GroupBy(s => s.Source).Select(g => (QrSources.Name(g.Key), g.Count()))
                .OrderByDescending(x => x.Item2).ToList()
        };
    }
}
