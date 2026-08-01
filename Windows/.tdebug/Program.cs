using System;
using System.Runtime.InteropServices;
[DllImport("LyricFeverTranslation.dll", CallingConvention = CallingConvention.Cdecl)]
static extern int lf_load_model(string modelPath, int interThreads, int intraThreads);
[DllImport("LyricFeverTranslation.dll", CallingConvention = CallingConvention.Cdecl)]
static extern int lf_translate_batch(string[] lines, int count, string src, string tgt, out IntPtr outLines, out int outCount);
[DllImport("LyricFeverTranslation.dll", CallingConvention = CallingConvention.Cdecl)]
static extern void lf_free_lines(IntPtr lines, int count);
[DllImport("LyricFeverTranslation.dll", CallingConvention = CallingConvention.Cdecl)]
static extern void lf_unload_model();
Console.WriteLine("loading ja-zh...");
Console.WriteLine("load rc=" + lf_load_model(@"D:\Tools\LyricFever\Windows\native\models\ja-zh", 1, 2));
var lines = new[] { "君の声が聞こえる", "涙があふれる夜もあるけど", "明日の光を信じて歩こう" };
Console.WriteLine("translating...");
var rc = lf_translate_batch(lines, lines.Length, "jp", "zh", out var outLines, out var outCount);
Console.WriteLine($"rc={rc} count={outCount}");
for (int i = 0; i < outCount; i++)
{
    var ptr = Marshal.ReadIntPtr(outLines, i * IntPtr.Size);
    Console.WriteLine($"  {lines[i]} -> {Marshal.PtrToStringUTF8(ptr)}");
}
lf_free_lines(outLines, outCount);
lf_unload_model();
Console.WriteLine("done");
