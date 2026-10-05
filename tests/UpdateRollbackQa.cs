using System;
using System.IO;
using UsagePeek;

internal static class UpdateRollbackQa
{
    private static int failures;

    private static int Main()
    {
        VerifyApplyAndRollback();
        VerifyApplyAndCommit();
        VerifyBadHashKeepsCurrentVersion();

        if (failures > 0)
        {
            Console.Error.WriteLine("Update rollback QA failed: " + failures);
            return 1;
        }
        Console.WriteLine("Update rollback QA passed.");
        return 0;
    }

    private static void VerifyApplyAndRollback()
    {
        RunInTemporaryDirectory(delegate(string root)
        {
            string target = Path.Combine(root, "UsagePeek.exe");
            string staged = Path.Combine(root, "updates", "UsagePeek-1.2.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(staged));
            byte[] oldBytes = ExecutableBytes("old-version");
            byte[] newBytes = ExecutableBytes("new-version");
            File.WriteAllBytes(target, oldBytes);
            File.WriteAllBytes(staged, newBytes);

            UpdateReplacementTransaction transaction =
                new UpdateReplacementTransaction(staged, target,
                    ExecutableIntegrity.ComputeSha256(newBytes),
                    Path.GetDirectoryName(staged));
            transaction.Apply();
            Check(SameBytes(File.ReadAllBytes(target), newBytes),
                "apply installs the verified new executable");
            Check(File.Exists(transaction.BackupPath),
                "apply preserves a rollback backup");

            Check(transaction.Rollback(),
                "rollback reports a successful recovery");
            Check(SameBytes(File.ReadAllBytes(target), oldBytes),
                "rollback restores the original executable");
            Check(!File.Exists(transaction.BackupPath) &&
                    !File.Exists(staged),
                "rollback cleans the completed transaction files");
        });
    }

    private static void VerifyApplyAndCommit()
    {
        RunInTemporaryDirectory(delegate(string root)
        {
            string target = Path.Combine(root, "UsagePeek.exe");
            string staged = Path.Combine(root, "updates", "UsagePeek-1.2.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(staged));
            byte[] newBytes = ExecutableBytes("committed-version");
            File.WriteAllBytes(target, ExecutableBytes("old-version"));
            File.WriteAllBytes(staged, newBytes);

            UpdateReplacementTransaction transaction =
                new UpdateReplacementTransaction(staged, target,
                    ExecutableIntegrity.ComputeSha256(newBytes),
                    Path.GetDirectoryName(staged));
            transaction.Apply();
            transaction.Commit();

            Check(SameBytes(File.ReadAllBytes(target), newBytes),
                "commit keeps the verified new executable");
            Check(!File.Exists(transaction.BackupPath) &&
                    !File.Exists(staged),
                "commit removes backup and staged files");
        });
    }

    private static void VerifyBadHashKeepsCurrentVersion()
    {
        RunInTemporaryDirectory(delegate(string root)
        {
            string target = Path.Combine(root, "UsagePeek.exe");
            string staged = Path.Combine(root, "updates", "UsagePeek-1.2.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(staged));
            byte[] oldBytes = ExecutableBytes("old-version");
            File.WriteAllBytes(target, oldBytes);
            File.WriteAllBytes(staged, ExecutableBytes("tampered-version"));

            bool rejected = false;
            try
            {
                UpdateReplacementTransaction transaction =
                    new UpdateReplacementTransaction(staged, target,
                        new string('0', 64), Path.GetDirectoryName(staged));
                transaction.Apply();
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            Check(rejected, "a mismatched update hash is rejected");
            Check(SameBytes(File.ReadAllBytes(target), oldBytes),
                "a rejected update leaves the current executable untouched");
        });
    }

    private static byte[] ExecutableBytes(string marker)
    {
        return System.Text.Encoding.UTF8.GetBytes("MZ" + marker);
    }

    private static bool SameBytes(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
        {
            return false;
        }
        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }
        return true;
    }

    private static void RunInTemporaryDirectory(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(),
            "UsagePeekUpdateQa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            try { Directory.Delete(root, true); }
            catch { }
        }
    }

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            Console.WriteLine("PASS " + name);
            return;
        }
        failures++;
        Console.Error.WriteLine("FAIL " + name);
    }
}
