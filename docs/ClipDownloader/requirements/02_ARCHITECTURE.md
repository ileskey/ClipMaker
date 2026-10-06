# ClipDownloader Architecture & Engineering Guidelines

## 1. 기술 스택

- Language: C#
- Runtime: .NET
- Desktop UI: WPF
- Video metadata / download: yt-dlp
- Media processing: FFmpeg
- Architecture: MVVM 권장
- Async: Task / async-await / CancellationToken

---

## 2. 핵심 설계 원칙

### 2.1 UI와 다운로드 구현을 분리한다

ViewModel이 직접 yt-dlp 또는 FFmpeg CLI 문자열을 만들지 않는다.

권장 흐름:

```text
View
 ↓
ViewModel
 ↓
Application Service
 ↓
Domain / Interfaces
 ↓
Infrastructure
 ↓
yt-dlp / FFmpeg
```

---

### 2.2 플랫폼별 코드에 UI가 의존하지 않도록 한다

사용자가 URL을 입력하면 자동으로 source를 결정한다.

초기 구현에서는 CHZZK와 YouTube 모두 yt-dlp를 사용해도 된다.

권장 인터페이스 개념:

```csharp
public interface IVideoSource
{
    bool CanHandle(Uri url);

    Task<VideoInfo> GetVideoInfoAsync(
        Uri url,
        CancellationToken cancellationToken);
}
```

MVP에서 실제 구현은 다음처럼 단순화 가능하다.

```text
IVideoSource
    ↓
YtDlpVideoSource
    ├─ CHZZK
    └─ YouTube
```

향후:

```text
IVideoSource
├─ YtDlpVideoSource
├─ ChzzkNativeVideoSource
└─ YouTubeNativeVideoSource
```

로 확장 가능해야 한다.

---

## 3. 제안 프로젝트 구조

```text
ClipMaker.slnx

apps/
└─ ClipDownloader
   ├─ Views
   ├─ ViewModels
   ├─ Commands
   └─ App.xaml

src/
├─ ClipDownloader.Application
│  ├─ Services
│  ├─ DTOs
│  └─ UseCases
│
├─ ClipMaker.Media.Core
│  ├─ Models
│  ├─ ValueObjects
│  └─ Interfaces
│
├─ ClipDownloader.Infrastructure
│  ├─ YtDlp
│  ├─ Sources
│  ├─ Downloads
│  └─ FileSystem
└─ ClipMaker.Media.Infrastructure
   ├─ FFmpeg
   ├─ FFprobe
   ├─ Tools
   └─ ProcessExecution

tests/
├─ ClipMaker.Media.Core.Tests
├─ ClipMaker.Media.Infrastructure.Tests
├─ ClipDownloader.Application.Tests
└─ ClipDownloader.Infrastructure.Tests
```

초기 MVP에서는 프로젝트 수를 줄여도 되지만, 최소한 논리적 경계는 유지한다.

저장소는 `README_FOR_REPOSITORY.md`의 단일 저장소 전략을 따른다. 이번 대상은 ClipDownloader이며, ClipEditor는 별도 요청 시 추가한다. ClipMaker.Media 계열은 공유 가능한 미디어 기능만 담당하며 사이트별 다운로드 로직을 포함하지 않는다. 상세 책임과 참조 규칙은 [프로젝트 구조 설계](../PROJECT_STRUCTURE_DESIGN.md)에 정리한다.

---

## 4. 핵심 모델

### VideoInfo

예시:

```csharp
public sealed class VideoInfo
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required TimeSpan Duration { get; init; }

    public double? FrameRate { get; init; }

    public string? Uploader { get; init; }
    public string? ThumbnailUrl { get; init; }

    public IReadOnlyList<VideoFormat> Formats { get; init; }
}
```

---

### VideoFormat

```csharp
public sealed class VideoFormat
{
    public required string Id { get; init; }

    public int? Width { get; init; }
    public int? Height { get; init; }

    public double? FrameRate { get; init; }

    public string? VideoCodec { get; init; }
    public string? AudioCodec { get; init; }
}
```

---

### Timecode

Timecode는 string이 아니라 Value Object로 관리한다.

필수 기능:

- `HH:MM:SS:FF` parsing
- validation
- TimeSpan 변환
- frame 변환
- formatting

가능하면 floating point 누적 오차를 최소화한다.

29.97fps / 59.94fps 등 fractional frame rate 처리 가능성을 고려한다.

Drop-frame timecode는 MVP 필수사항이 아니다.

---

### ClipSegment

```csharp
public sealed class ClipSegment
{
    public required Timecode Start { get; init; }
    public required Timecode End { get; init; }

    public string? Name { get; init; }
}
```

---

### ClipJob

```csharp
public sealed class ClipJob
{
    public required Uri SourceUrl { get; init; }
    public required ClipSegment Segment { get; init; }

    public string? FormatId { get; init; }

    public required string OutputPath { get; init; }

    public ClipExtractionMode Mode { get; init; }
}
```

---

## 5. 주요 서비스

### IVideoInfoService

역할:

- URL 검증
- 영상 정보 조회
- yt-dlp JSON parsing

---

### IClipDownloadService

역할:

- 지정 구간 다운로드
- yt-dlp 호출
- 진행률 전달
- 취소 처리

---

### IMediaProcessingService

역할:

- FFmpeg 호출
- audio/video merge
- trim
- remux
- 필요한 경우 re-encode

---

### IProcessRunner

외부 프로세스 실행은 공통 계층으로 추상화한다.

기능:

- stdout 읽기
- stderr 읽기
- 종료 코드 반환
- CancellationToken 처리
- 강제 종료
- progress parsing callback

yt-dlp와 FFmpeg 서비스가 직접 `Process.Start()` 코드를 중복 구현하지 않도록 한다.

---

## 6. yt-dlp 사용 원칙

yt-dlp는 MVP에서 다음 용도로 사용한다.

- CHZZK 영상 정보 조회
- YouTube 영상 정보 조회
- 포맷 목록 조회
- 다운로드 URL 처리
- audio/video stream 선택 및 병합 지원

가능하면 machine-readable JSON 출력을 사용한다.

사람이 읽는 stdout을 문자열 split으로 파싱하는 방식은 최소화한다.

---

## 7. FFmpeg 사용 원칙

FFmpeg는 다음 용도로 사용한다.

- 구간 trim
- stream copy
- 정확한 구간 추출을 위한 재인코딩
- audio/video mux
- 출력 MP4 생성

### Fast Mode

가능하면:

```text
-c copy
```

기반.

단, keyframe 한계가 있음을 명확히 한다.

### Accurate Mode

정확한 경계를 위해 필요한 부분은 재인코딩할 수 있다.

초기 구현에서는 전체 구간 재인코딩도 허용한다.

후속 최적화에서:

- 앞 경계만 재인코딩
- 중간 구간 stream copy
- 뒤 경계 재인코딩

등 hybrid trim을 검토할 수 있다.

---

## 8. UI 상태 관리

ViewModel 상태 예:

```text
Idle
LoadingMetadata
Ready
Downloading
Processing
Completed
Failed
Cancelled
```

bool 속성이 난립하지 않도록 상태를 명확히 관리한다.

예:

- `IsLoading`
- `IsDownloading`
- `CanDownload`

등은 내부 상태에서 파생할 수 있다.

---

## 9. 오류 처리

Infrastructure의 raw error를 그대로 UI에 노출하지 않는다.

예:

yt-dlp:

```text
ERROR: Unsupported URL
```

→

```text
지원하지 않는 영상 URL입니다.
```

예:

FFmpeg executable not found

→

```text
FFmpeg를 찾을 수 없습니다.
설정에서 FFmpeg 경로를 확인해주세요.
```

로그에는 원래 stderr를 유지한다.

---

## 10. 외부 도구 관리

필요 실행 파일:

- yt-dlp.exe
- ffmpeg.exe
- 필요 시 ffprobe.exe

지원 전략 중 하나를 선택한다.

초기 권장:

```text
ClipDownloader/
├─ ClipDownloader.exe
└─ tools/
   ├─ yt-dlp.exe
   ├─ ffmpeg.exe
   └─ ffprobe.exe
```

코드에는 절대경로를 하드코딩하지 않는다.

---

## 11. 테스트 우선 대상

Unit Test가 특히 가치 있는 영역:

### Timecode

- 정상 parsing
- invalid parsing
- frame → time 변환
- 30fps
- 60fps
- 29.97fps
- Start / End validation

### File name sanitization

Windows invalid character 제거.

### URL Detection

- CHZZK
- YouTube
- youtu.be
- unsupported

### yt-dlp JSON parser

실제 샘플 JSON fixture를 이용한다.

---

## 12. 구현 시 금지 사항

- View code-behind에 business logic 작성 금지
- UI thread에서 blocking Process.WaitForExit 금지
- yt-dlp stdout 포맷을 무근거하게 가정하지 말 것
- 사이트별 URL parsing 로직을 ViewModel에 작성하지 말 것
- FFmpeg 명령 문자열을 UI 코드에 작성하지 말 것
- 모든 예외를 `catch (Exception)` 후 무시하지 말 것
- 임시파일 삭제 실패를 조용히 무시하지 말고 로그 남길 것
