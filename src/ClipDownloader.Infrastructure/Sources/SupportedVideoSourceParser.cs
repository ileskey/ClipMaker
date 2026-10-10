using System.Globalization;
using ClipDownloader.Application.Models;

namespace ClipDownloader.Infrastructure.Sources;

public sealed class SupportedVideoSourceParser
{
    public OperationResult<VideoSourceReference> Parse(Uri input)
    {
        ArgumentNullException.ThrowIfNull(input);
        // AbsolutePath can already have collapsed dot/escaped segments. Inspect the source text.
        return Parse(input.OriginalString);
    }

    public OperationResult<VideoSourceReference> Parse(string? input)
    {
        string? text = input?.Trim();
        if (string.IsNullOrEmpty(text)) return Invalid("영상 URL을 입력해야 합니다.");
        if (text.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character == '\\'))
            return Invalid("URL 내부 공백·제어 문자·역슬래시는 사용할 수 없습니다.");
        int schemeEnd = text.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return Invalid("URL에는 명시적인 scheme과 호스트가 필요합니다.");
        if (!HasValidEscapes(text) || !Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return Invalid("올바른 절대 URL 형식이 아닙니다.");
        if (uri.Scheme != Uri.UriSchemeHttps) return Unsupported("HTTPS 영상 URL만 지원합니다.");
        if (!uri.IsWellFormedOriginalString()) return Invalid("올바른 HTTPS URL 형식이 아닙니다.");

        int authorityStart = schemeEnd + 3;
        int authorityEnd = FindDelimiter(text, authorityStart);
        string authority = text[authorityStart..authorityEnd];
        if (authority.Contains('@') || uri.UserInfo.Length != 0)
            return Invalid("사용자 인증정보를 포함한 URL은 사용할 수 없습니다.");
        if (authority.Contains('%')) return Invalid("호스트에 percent escape를 사용할 수 없습니다.");
        if (uri.HostNameType != UriHostNameType.Dns) return Unsupported("공식 DNS 호스트만 지원합니다.");

        string host = authority;
        int portStart = authority.LastIndexOf(':');
        if (portStart >= 0)
        {
            host = authority[..portStart];
            string port = authority[(portStart + 1)..];
            if (port.Length == 0 || !port.All(char.IsAsciiDigit) ||
                !int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out int portNumber))
                return Invalid("HTTPS 포트 형식이 올바르지 않습니다.");
            if (portNumber != 443) return Unsupported("기본 HTTPS 포트 443만 지원합니다.");
        }
        if (uri.Port != 443) return Unsupported("기본 HTTPS 포트 443만 지원합니다.");
        if (!host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase))
            return Unsupported("정확한 공식 영상 호스트만 지원합니다.");

        int pathEnd = text.IndexOfAny(['?', '#'], authorityEnd);
        if (pathEnd < 0) pathEnd = text.Length;
        string path = text[authorityEnd..pathEnd];
        string query = "";
        if (pathEnd < text.Length && text[pathEnd] == '?')
        {
            int fragment = text.IndexOf('#', pathEnd + 1);
            query = text[(pathEnd + 1)..(fragment < 0 ? text.Length : fragment)];
        }

        if (host.Equals("chzzk.naver.com", StringComparison.OrdinalIgnoreCase))
        {
            const string prefix = "/video/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) return Unsupported("CHZZK VOD의 /video/{ID} 경로만 지원합니다.");
            string id = Uri.UnescapeDataString(path[prefix.Length..]);
            return Reference(VideoPlatform.Chzzk, id);
        }
        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            if (!path.StartsWith('/') || path.Length < 2) return Invalid("YouTube 영상 ID가 없습니다.");
            return Reference(VideoPlatform.YouTube, Uri.UnescapeDataString(path[1..]));
        }
        if (host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("www.youtube.com", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("m.youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            if (path != "/watch") return Unsupported("YouTube의 /watch 영상 경로만 지원합니다.");
            string? id = null;
            int count = 0;
            foreach (string parameter in query.Split('&'))
            {
                int equals = parameter.IndexOf('=');
                string key = Uri.UnescapeDataString(equals < 0 ? parameter : parameter[..equals]);
                if (key != "v") continue;
                count++;
                if (count > 1) return Invalid("중복된 v 파라미터는 사용할 수 없습니다.");
                id = equals < 0 ? "" : Uri.UnescapeDataString(parameter[(equals + 1)..]);
            }
            return Reference(VideoPlatform.YouTube, id);
        }
        return Unsupported("지원하는 CHZZK·YouTube 공식 호스트가 아닙니다.");
    }

    private static int FindDelimiter(string text, int start)
    {
        int index = text.IndexOfAny(['/', '?', '#'], start);
        return index < 0 ? text.Length : index;
    }

    private static bool HasValidEscapes(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != '%') continue;
            if (index + 2 >= text.Length || !char.IsAsciiHexDigit(text[index + 1]) || !char.IsAsciiHexDigit(text[index + 2])) return false;
            index += 2;
        }
        return true;
    }

    private static OperationResult<VideoSourceReference> Reference(VideoPlatform platform, string? id) =>
        VideoSourceReference.TryCreate(platform, id, out var value) ? OperationResult<VideoSourceReference>.Succeeded(value) :
            Invalid("영상 ID의 기본 문자·길이 형식이 올바르지 않습니다.");

    private static OperationResult<VideoSourceReference> Invalid(string message) =>
        OperationResult<VideoSourceReference>.Failed(new(OperationErrorCode.InvalidInput, OperationErrorCategory.Input, message, "Url"));
    private static OperationResult<VideoSourceReference> Unsupported(string message) =>
        OperationResult<VideoSourceReference>.Failed(new(OperationErrorCode.UnsupportedSource, OperationErrorCategory.Input, message, "Url"));
}
