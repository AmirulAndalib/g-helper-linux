using GHelper.Linux.USB;

namespace GHelper.Linux.Tests;

public static class TufEcLightingTests
{
    public static void RunAll()
    {
        foreach (string model in new[]
        {
            "TUF Gaming A16 FA608UHI",
            "ASUS TUF Gaming A16 FA608UHI_FA608UHI",
            "tuf gaming a16 fa608uhi",
        })
            Harness.Scenario($"EC initialization: {model}", _ =>
            {
                var reports = new List<byte[]>();
                Harness.Assert(TufEcLighting.Restore(model, report =>
                {
                    reports.Add(report);
                    return true;
                }), "successful initialization");
                Harness.AssertEqual(1, reports.Count, "report count");
                Harness.Assert(reports[0].SequenceEqual(new byte[] { 0x46, 0x01 }), "two-byte autonomous-mode feature report");
            });

        foreach (string model in new[] { "", "TUF Gaming A16 FA608WV", "TUF Gaming A16 FA608UH", "ROG Strix G614", "FA608UHIX" })
            Harness.Scenario($"EC initialization skips other models: {model}", _ =>
            {
                int calls = 0;
                Harness.Assert(!TufEcLighting.Restore(model, _ =>
                {
                    calls++;
                    return true;
                }), "not applied");
                Harness.AssertEqual(0, calls, "no hardware writes");
            });

        Harness.Scenario("EC initialization retries after a failed write and on resume", _ =>
        {
            int calls = 0;
            bool Send(byte[] report) => ++calls > 1;
            Harness.Assert(!TufEcLighting.Restore("FA608UHI", Send), "failed write stays failed");
            Harness.Assert(TufEcLighting.Restore("FA608UHI", Send), "retry succeeds");
            Harness.Assert(TufEcLighting.Restore("FA608UHI", Send), "resume reapplies mode");
            Harness.AssertEqual(3, calls, "one report per attempt");
        });
    }
}
