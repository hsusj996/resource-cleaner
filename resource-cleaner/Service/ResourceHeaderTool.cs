using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public static class ResourceHeaderTool
{
    // 스캔할 확장자 목록
    private static readonly HashSet<string> ScanExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".c", ".cc", ".cpp", ".cxx", ".h", ".hpp", ".inl",
        ".rc", ".rc2", ".idl", ".mc", ".def", ".asm"
    };

    // 스킵할 디렉터리
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin","obj",".git",".github",".vs","node_modules","packages","Debug","Release","x64","x86","arm","arm64"
    };

    // #define NAME NUMBER 정규식
    private static readonly Regex DefineLine =
        new(@"^\s*#\s*define\s+([A-Za-z_]\w*)\s+([0-9]+)\b.*$", RegexOptions.Compiled | RegexOptions.Multiline);

    // 한 줄 단위 define 탐지(삽입 위치 산정용)
    private static readonly Regex DefineSingleLine =
        new(@"^\s*#\s*define\s+([A-Za-z_]\w*)\s+([0-9]+)\b.*$", RegexOptions.Compiled);

    public sealed class Result
    {
        public string ResourceHeaderPath { get; init; } = "";
        public int TotalDefines { get; init; }
        public int RemovedDefines { get; init; }
        public int KeptDefines { get; init; }
        public IReadOnlyList<(string Name, int OldValue, bool Used)> Before { get; init; } = Array.Empty<(string, int, bool)>();
        public IReadOnlyList<(string Name, int NewValue)> After { get; init; } = Array.Empty<(string, int)>();
        public string BackupPath { get; init; } = "";
    }

    public static Result CleanAndRenumber(string rootPath, string? resourceHeaderPath = null, bool apply = true, int maxFileSizeBytes = 5_000_000)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"루트 경로가 올바르지 않습니다: {rootPath}");

        // 1) resource.h 찾기
        string resPath = resourceHeaderPath ?? FindResourceHeader(rootPath);
        if (resPath is null)
            throw new FileNotFoundException("경로 아래에서 resource.h를 찾지 못했습니다.");

        string resText = File.ReadAllText(resPath, DetectEncoding(resPath));

        // 2) resource.h의 #define(NAME, NUMBER) 파싱
        var defines = new List<(string Name, int Value, int LineIndex)>();
        var lineIndexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = File.ReadAllLines(resPath, DetectEncoding(resPath));

        for (int i = 0; i < lines.Length; i++)
        {
            var m = DefineSingleLine.Match(lines[i]);
            if (!m.Success) continue;
            string name = m.Groups[1].Value;
            if (!int.TryParse(m.Groups[2].Value, out var num)) continue;
            defines.Add((name, num, i));
            lineIndexByName[name] = i;
        }

        if (defines.Count == 0)
            throw new InvalidDataException("resource.h에서 숫자형 #define을 찾지 못했습니다.");

        // 3) 스캔 대상 파일 수집
        var files = EnumerateFilesSafe(rootPath)
            .Where(p =>
            {
                var ext = Path.GetExtension(p);
                if (!ScanExt.Contains(ext)) return false;
                var fi = new FileInfo(p);
                if (fi.Length > maxFileSizeBytes) return false;
                // resource.h 자신은 제외 (자기 정의 라인을 '사용'으로 치지 않기 위함)
                if (Path.GetFullPath(p).Equals(Path.GetFullPath(resPath), StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            })
            .ToArray();

        // 4) 실사용 여부 판정 (주석/문자열 제거 후 \bNAME\b 검색)
        var used = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        foreach (var d in defines) used[d.Name] = false;

        // 미리 정규식 캐시
        var regexes = defines.ToDictionary(d => d.Name, d => new Regex(@"\b" + Regex.Escape(d.Name) + @"\b", RegexOptions.Compiled), StringComparer.Ordinal);

        int remaining = defines.Count;
        foreach (var file in files)
        {
            string text;
            try { text = StripCommentsAndStrings(File.ReadAllText(file, DetectEncoding(file))); }
            catch { continue; }

            foreach (var d in defines)
            {
                if (used[d.Name]) continue;
                if (regexes[d.Name].IsMatch(text))
                {
                    used[d.Name] = true;
                    remaining--;
                    if (remaining == 0) break;
                }
            }
            if (remaining == 0) break;
        }

        // 옵션: resource.h 내 다른 define의 RHS(우변)에서의 간접 사용도 인정하고 싶다면 해제
        // (일반적인 resource.h는 대부분 숫자 상수라 필요 없음)
        // MarkIndirectUsageInResource(defines, resText, used);

        // 5) 미사용 제거 & 이름 기준 정렬 & 1부터 재부여
        var before = defines.Select(d => (d.Name, d.Value, Used: used[d.Name])).ToList();
        var kept = before.Where(x => x.Used).Select(x => x.Name).ToList();

        var sorted = kept.OrderBy(n => n, StringComparer.Ordinal).ToList();
        var after = sorted.Select((name, idx) => (Name: name, NewValue: idx + 1)).ToList();

        // 6) resource.h 재작성(백업 생성)
        string backupPath = "";
        if (apply)
        {
            backupPath = BackupFile(resPath);
            var newHeader = RewriteResourceHeader(lines, after, out int firstDefineLineIndex);
            File.WriteAllText(resPath, newHeader, DetectEncoding(resPath));
        }

        return new Result
        {
            ResourceHeaderPath = resPath,
            TotalDefines = defines.Count,
            RemovedDefines = defines.Count - kept.Count,
            KeptDefines = kept.Count,
            Before = before,
            After = after,
            BackupPath = backupPath
        };
    }

    private static string FindResourceHeader(string rootPath)
    {
        var matches = EnumerateFilesSafe(rootPath)
            .Where(p => string.Equals(Path.GetFileName(p), "resource.h", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0) return null!;
        // 여러 개인 경우, 가장 상위(경로 길이 짧은 것) 우선
        return matches.OrderBy(p => p.Length).First();
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string name = Path.GetFileName(dir);

            if (SkipDirs.Contains(name)) continue;

            IEnumerable<string> subdirs = Enumerable.Empty<string>();
            IEnumerable<string> files = Enumerable.Empty<string>();
            try
            {
                subdirs = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir);
            }
            catch { /* 권한 문제 등 무시 */ }

            foreach (var f in files) yield return f;
            foreach (var d in subdirs) stack.Push(d);
        }
    }

    private static Encoding DetectEncoding(string path)
    {
        // 간단 가정: BOM 우선, 없으면 Default
        using var sr = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        sr.Peek();
        return sr.CurrentEncoding;
    }

    private static string BackupFile(string path)
    {
        var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var bak = path + $".bak_{ts}";
        File.Copy(path, bak, overwrite: false);
        return bak;
    }

    private static string RewriteResourceHeader(string[] originalLines, List<(string Name, int NewValue)> newDefines, out int firstDefineIndex)
    {
        firstDefineIndex = -1;
        var isDefineLine = new bool[originalLines.Length];
        for (int i = 0; i < originalLines.Length; i++)
        {
            var m = DefineSingleLine.Match(originalLines[i]);
            if (m.Success)
            {
                isDefineLine[i] = true;
                if (firstDefineIndex == -1) firstDefineIndex = i;
            }
        }

        if (firstDefineIndex == -1) // 정의 라인이 하나도 없었다면, 파일 끝에 추가
            firstDefineIndex = originalLines.Length;

        // 정렬된 define 블록 생성 (칼럼 정렬)
        int maxName = newDefines.Count == 0 ? 0 : newDefines.Max(x => x.Name.Length);
        var block = new List<string>();
        block.Add("// ==== Auto-generated by ResourceHeaderTool ====");
        foreach (var (name, val) in newDefines)
        {
            block.Add($"#define {name.PadRight(maxName + 1)} {val}");
        }
        block.Add("// ==== End of auto-generated block ====");

        var sb = new StringBuilder();
        // 1) 첫 define 이전까지는 그대로
        for (int i = 0; i < firstDefineIndex && i < originalLines.Length; i++)
            sb.AppendLine(originalLines[i]);

        // 2) 새 블록 삽입
        foreach (var line in block) sb.AppendLine(line);

        // 3) 이후 라인 중 기존 define 라인은 모두 스킵, 나머지는 유지
        for (int i = firstDefineIndex; i < originalLines.Length; i++)
        {
            if (!isDefineLine[i]) sb.AppendLine(originalLines[i]);
        }

        return sb.ToString();
    }

    // C/C++/RC 스타일의 주석/문자열 제거
    private static string StripCommentsAndStrings(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool inSL = false, inML = false, inStr = false, inChar = false;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            char next = (i + 1 < s.Length) ? s[i + 1] : '\0';

            if (inSL)
            {
                if (c == '\n') { inSL = false; sb.Append('\n'); }
                continue;
            }
            if (inML)
            {
                if (c == '*' && next == '/') { inML = false; i++; }
                continue;
            }
            if (inStr)
            {
                if (c == '\\') { i++; continue; }
                if (c == '"') inStr = false;
                continue;
            }
            if (inChar)
            {
                if (c == '\\') { i++; continue; }
                if (c == '\'') inChar = false;
                continue;
            }

            if (c == '/' && next == '/') { inSL = true; i++; continue; }
            if (c == '/' && next == '*') { inML = true; i++; continue; }
            if (c == '"') { inStr = true; continue; }
            if (c == '\'') { inChar = true; continue; }

            sb.Append(c);
        }
        return sb.ToString();
    }
}
