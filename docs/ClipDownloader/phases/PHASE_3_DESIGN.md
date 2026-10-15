# ClipDownloader Phase 3 — Metadata UI 상세 설계

작성일: 2026-10-15 (Asia/Seoul). 2026-10-16 상태 갱신: **구현·필수 검증 완료 / Passed**. 아래 1~9절은 설계 당시의 계획·관찰 기록이다. 실제 구현·검증 결과와 현재 제약은 [구현 기록](PHASE_3_IMPLEMENTATION.md) 및 아래 10절을 따른다.

## 1. 목표·기준·시작 조건

다운로드 전에 Title, Uploader, Duration, FPS, Thumbnail, Available resolutions를 구분해서 확인하게 한다. 누락·오류·재조회·취소·오래된 응답·종료에서 서로 다른 영상의 정보가 섞이지 않아야 한다. Queue, Player, ClipEditor, 브라우저, 인증 UX, 자동 도구 설치, 미디어 지원 확대는 제외한다.

기준 문서는 [Phase 3 인계](PHASE_3_DESIGN_HANDOFF.md), [운영 규칙](../PHASE_DESIGN.md), [Baseline](../PHASE_BASELINE.md), [제품 요구사항](../requirements/01_PRODUCT_REQUIREMENTS.md), [아키텍처](../requirements/02_ARCHITECTURE.md), [구현 계획](../requirements/03_IMPLEMENTATION_PLAN.md), [구조 설계](../PROJECT_STRUCTURE_DESIGN.md), [Codex 지침](../requirements/README_FOR_CODEX.md), [저장소 지침](../requirements/README_FOR_REPOSITORY.md)이다. [Phase 2 설계](PHASE_2_DESIGN.md), [실제 구현·검증](PHASE_2_IMPLEMENTATION.md), [미디어 증거](evidence/PHASE_2_WPF_MEDIA.json), [역사적 Phase 2 인계](PHASE_2_DESIGN_HANDOFF.md), [Phase 1 최종 인계](PHASE_1_FINAL_VERIFICATION_HANDOFF.md)를 대조했다. 과거 미구현 설명·예시 API보다 현재 소스와 최종 채택 기록을 적용한다. 사용자 제공 AGENTS.md 지시를 적용하며 저장소 검색에서 별도 AGENTS.md 파일은 발견하지 않았다.

| 시작 시 실제 확인 | 값 / 판단 |
|---|---|
| 현재 branch / HEAD | `phase/2` / `5d5a06e6a6d0b8ec8ea952e738ecf99756a53712` |
| HEAD를 직접 가리키는 tag | 없음 |
| Phase 2 Completion / dereferenced tag | `b4ef12692da9268c72c2a42e6c0ab73351d7bb9f` / `phase-2-complete` 일치 |
| 검증 제품 소스 | `86fd9f8014aba1135fe30ba6d5b14be4df3ab152`; Completion의 ancestor 확인 |
| staged / unstaged / untracked | 최초 `git status --porcelain=v1 --untracked-files=all` 출력 없음 |
| Completion 이후 | `183532d`, `5d5a06e` 두 문서 커밋; docs·증거 7파일, +139/−10. `apps src tests` 제품 diff 없음 |
| Phase 3 설계 소스 snapshot | 위 HEAD. Phase 2 제품 소스와 동일 |
| Phase 3 실행 branch / Base | 사용자 후속 결정으로 `phase/3` 사용. 후속 확인에서도 현재 branch가 `phase/3`, HEAD는 위 값과 동일함을 확인. Base 후보는 위 HEAD이며 구현 시작 때 실제 상태·추가 변경을 재대조하고 확정 후 Baseline에 기록 |

이번에는 현 브랜치에서 설계 파일 하나만 작성한다. 설계 snapshot을 Phase 3 구현 Base나 Completion으로 소급 기록하지 않는다. 기존 태그·이력·사용자 변경·ignored `.artifacts`를 보존하며 원격 서버는 재조회하지 않았다.

Phase 2는 Completed / Passed다. 기존 기록은 build 경고/오류 0, 기존 1,195 + WPF 35 = 1,230개 통과, 실패/skip 0, 양 사이트·Fast/Accurate 실제 WPF 생성·전체 A/V decode·사용자 재생/동기 확인이다. 격리 스냅샷 tree 대응도 기록에 있다. 이는 이번 재실행 결과가 아니다. live 취소/닫기와 지연 fake의 반복 Window.Close 증거를 구분하고, 실제 프로세스 종료 실패 강제 검증으로 확대하지 않는다. NU1900 및 일회성 NuGetAudit 비활성 이력이 있으므로 취약성 조회 통과를 주장하지 않는다.

일부 CHZZK MPD metadata 실패와 먼 위치 Accurate 거절은 기존 지원 경계다. Phase 3 시작을 막지 않지만 범용 입력 지원을 주장할 수 없다. Thumbnail은 제품 요구사항에서는 선택 필드이나 Phase 3 표시 목록에는 포함된다. 따라서 표시 파이프라인과 누락 안내를 구현 대상에 포함하고, 모든 원본에서 이미지 획득 성공을 필수 전제로 삼지는 않는다.

## 2. 현재 코드와 변경 책임

| 현재 연결 지점 | 확인한 사실 / 변경 계획 |
|---|---|
| `Application/Contracts/IVideoSourceParser.cs`, `IVideoInfoService.cs` | `Parse(Uri)`, `GetVideoInfoAsync(Uri, CancellationToken)` 유지 |
| `Application/Models/VideoInfo.cs` | nullable Title·Uploader·Duration·ThumbnailUrl, State·Formats·Issues. 대표 FPS 없음. 모델 변경 없이 projection |
| `Application/Validation/FormatSelector.cs`, `Models/FormatSelection.cs` | metadata 선언에 따른 video/audio 조합. 실제 실행 가능성 보장 아님. 순위·경고 유지 |
| `Infrastructure/YtDlp/YtDlpMetadataParser.cs` | uploader가 absent/null일 때만 channel fallback. UI에서 새로운 fallback을 만들지 않음. thumbnail URI 필터 유지 |
| `ViewModels/MainViewModel.Input.cs` | metadata·selection·revision·Quality·시간 검증의 소유자 유지. 요약 문자열을 상세 projection과 연결 |
| `ViewModels/MainViewModel.cs`, `.Job.cs` | foreground Task/CTS/generation·shutdown·PreparedClip 계약 유지. 별도 thumbnail Task/CTS/epoch와 종료 대기 추가 |
| `Views/MainWindow.xaml(.cs)` | metadata 영역 교체, 접근성·스크롤·이미지 binding. code-behind는 기존 focus/닫기 glue 범위 유지 |
| `Composition/ServiceRegistration.cs`, `App.xaml.cs` | 새 HTTP 소유자와 WPF decode 어댑터 등록. App 종료 순서는 유지 |
| `tests/ClipDownloader.Tests`, `tests/ClipDownloader.Infrastructure.Tests` | projection·Dispatcher/Window·HTTP 정책 검증. 일반 TFM 테스트에 WPF 참조 추가 금지 |

표의 상대 코드 경로는 `src/ClipDownloader.Application`, `src/ClipDownloader.Infrastructure`, `apps/ClipDownloader` 아래를 의미한다. 신규 이름은 아래 계약대로 추가할 예정이며 현재 존재한다고 가정하지 않는다.

## 3. 논리 단위 A — 여섯 필드와 선택 의미

### 3.1 표시 모델

WPF 안에 작은 불변 `MetadataPresentation` projection과 순수 factory를 둔다. 원본 `VideoInfo`와 현재 `FormatSelection?`을 입력으로 받아 필드별 표시 문자열·해상도 목록·안전한 진단을 만든다. WPF 표시 모델이 Application 모델을 대체하거나 Prepare 입력을 재구성하지 않는다. MainViewModel partial `MainViewModel.Metadata.cs`가 projection·이미지 orchestration을 담당한다. 별도 navigation/범용 metadata 서비스 계층은 만들지 않는다.

| 표시 label | 원천 / 표시 규칙 | 누락·실패 |
|---|---|---|
| 제목 | `VideoInfo.Title`; 줄바꿈·한글·긴 제목 지원, 읽기 전용 복사 가능 | null/공백은 `제목 미확인`. source ID로 제목을 꾸며 넣지 않음 |
| 업로더 / 채널 | `VideoInfo.Uploader`; parser의 기존 channel fallback 결과 그대로 | `업로더 미확인`; 잘못된 uploader를 UI에서 channel로 재해석하지 않음 |
| 총 길이 | 양수 `Duration`; total hours 기반 `HH:MM:SS` + 필요한 소수 초(최대 7자리, 끝 0 제거). 24시간에서 wrap하지 않음 | Completed·양수 길이 gate 불충족은 metadata Failed, 부분 Ready 금지 |
| 선택 포맷 FPS | `selection.VideoFormat.FrameRate`; `30000/1001 fps (약 29.970)`처럼 exact rational을 주값으로, 근사는 표시 보조만 | 선택 없음은 `선택 포맷 없음`, 선택했으나 null은 `FPS 미확인`과 기존 pending checks 안내 |
| 썸네일 | 현재 metadata epoch의 제한된 HTTP bytes를 decode한 frozen `BitmapSource` | 빈 상태·로딩·없음·취소·실패를 텍스트 placeholder로 구분 |
| 제공 해상도 (metadata 선언) | 아래 3.2 기준의 높이 집합을 `1080p · 720p` 형식으로 내림차순 | 빈 집합은 `높이를 확인한 영상 포맷 없음`; Best 선택 실패와 Prepare 차단을 함께 설명 |

FPS는 영상 전체·출력 확정 FPS로 표현하지 않는다. 원본 유리수는 `FrameRate.Numerator/Denominator`를 사용하며 29.97을 30이나 30000/1001로 UI에서 바꾸지 않는다. `Duration`은 timestamp 길이이고 FF를 붙이지 않는다. 표시용 반올림을 시간 계산·End 검증에 재사용하지 않는다. VFR/CFR·프레임 정확도는 metadata만으로 확정하지 않는다.

선택 해상도는 별도로 `선택 해상도: 1920×1080 (1080p)`와 요청 Quality→실제 video/audio ID를 보여준다. width만 null이면 `너비 미확인 × 1080`; 높이도 없으면 미확인으로 표시한다. 기존 `SelectionText`의 코덱·음성 선언·경고는 상세 영역에 유지한다. 확인 화면은 계속 PreparedClip에서만 읽는다.

### 3.2 Available resolutions와 Quality의 일관성

현재 `MainViewModel.Input.cs`와 동일하게 `VideoStream != Absent && Height > 0`인 포맷의 높이를 distinct·내림차순으로 취한다. 오디오 전용과 높이 미확인 포맷은 해상도 목록에서 제외한다. Unknown video는 기존 선택 후보이므로 포함하되 `영상 스트림 미확인 후보 포함` 경고를 유지한다. 높이 없는 영상 후보의 존재도 진단에 남긴다.

동일 높이의 codec·FPS·width·transport 차이는 여러 해상도 항목으로 늘리지 않는다. Quality 목록은 `Best + 동일 높이 집합`; Best는 해상도 자체가 아니므로 제공 해상도 목록에는 넣지 않는다. 한 순수 helper에서 두 목록을 만들어 필터가 갈라지지 않게 한다. 포맷 ID 직접 선택이나 높이별 대표 FPS는 추가하지 않는다. selector의 기존 순위로 결정된 현재 선택만 FPS와 연결한다.

metadata가 Ready여도 선택 실패는 가능하다. 이때 제목·업로더·길이·thumbnail은 유지하고 FPS/선택을 비우며 Quality 오류·Prepare 차단을 제공한다. 자동 하위 화질, 모드 변경, 새 selector는 도입하지 않는다.

### 3.3 Issues·긴 텍스트·접근성

`MetadataIssue(Field, Reason, FormatIndex?)`는 접을 수 있는 `정보 확인 사항`에 필드·안전한 이유·원본 format index로 표시한다. index는 parser 원시 배열의 위치이며 필터 후 `Formats[index]`에 대응시키지 않는다. 원본 값·URL·JSON은 출력하지 않는다. field는 알려진 이름을 사용자 문구로 매핑하고 모르는 값은 일반 항목으로 표시한다. 표시 필드의 null은 Issues가 없어도 미확인이다. parser는 absent/null마다 Missing issue를 만드는 계약이 아니므로 issue 부재를 완전성으로 해석하지 않는다. Rounded duration은 현재 값과 반올림 안내를 함께 표시한다.

영상 정보 영역은 URL/조회 바로 아래, 시간 입력 위다. 썸네일은 최대 240×135 DIP의 고정 placeholder 안에서 Uniform으로 표시하고 crop·자동 확대·클릭 재생은 없다. 제목/업로더는 이미지 아래 전체 폭에 배치해 최소 폭 620에서도 긴 글이 입력을 밀어내지 않게 한다. 읽기 전용 텍스트는 wrap·복사·키보드 접근을 지원하고 큰 값은 내부 세로 스크롤을 허용한다. Tooltip만으로 전체 값을 제공하지 않는다. metadata의 이미지 성공/실패로 focus를 이동하지 않는다.

각 필드에 Name/AutomationId, 이미지에는 `현재 영상 썸네일` 대체 이름, placeholder에는 상태 텍스트를 제공한다. 오류는 색상 외 문구로 전달한다. 조회 실패의 기존 오류 focus는 유지한다. 이미지 상태는 별도 Polite 알림으로 전달하고 메인 StatusText·실행 결과를 덮지 않는다. 100/150/200% DPI, 최소 창 크기, 한글/장문/키보드 tab 순서를 실제 WPF에서 확인한다.

검토 결론 A: 기존 데이터 계약으로 여섯 필드의 의미를 표현할 수 있다. 대표 FPS를 추가하는 대안은 선택 FPS와 어긋날 수 있어 채택하지 않는다. 중복 해상도별 포맷을 전부 나열하는 대안은 현재 높이 단위 Quality UX에 불필요하므로 상세 선택 경고로 보완한다.

## 4. 논리 단위 B — Thumbnail 계약과 획득·decode 정책

### 4.1 책임과 새 계약

새 Application 계약 `IThumbnailContentService.GetAsync(Uri, CancellationToken)` → `Task<ThumbnailContentResult>`를 추가한다. 결과는 `Succeeded / Failed / Cancelled`, 성공 시 소유권을 넘기는 최대 크기 제한 bytes와 JPEG/PNG kind, 실패 시 아래 안전한 reason code를 갖는다. 성공·실패·취소의 payload 조합을 factory로 제한하고 Stream·HttpResponseMessage·WPF 타입은 반환하지 않는다. bytes는 호출마다 독립 소유하며 외부 변경 없이 decode 동안만 보관한다. thumbnail 전용 오류를 clip용 `OperationErrorCode`에 대량 추가하지 않는다.

`HttpThumbnailContentService`는 Downloader.Infrastructure의 `Thumbnails/`에서 구현한다. DI singleton이 전용 HttpClient/handler를 소유하고 마지막에 한 번 dispose한다. 기존 source acquisition HTTP 소유자·64 MiB 예산·자원 조회에 결합하지 않는다. ViewModel은 이 Application 계약만 사용하고 HTTP를 직접 다루지 않는다.

WPF 앱의 `Imaging/IThumbnailDecoder`와 `WpfThumbnailDecoder`는 제한 bytes→frozen `BitmapSource` 변환을 담당한다. decode 실패/지원 불가를 앱 표시 결과로 변환한다. decoder는 UI 타입을 써도 되지만 Infrastructure와 Application은 WPF를 참조하지 않는다. 이미 있는 .NET HTTP/WPF API를 사용하며 신규 NuGet 패키지·프로젝트는 필요하지 않다.

| 대안 | trade-off / 판단 |
|---|---|
| XAML Image.Source에 원격 URI 직접 binding | 간단하지만 숨은 네트워크·캐시·redirect·stream 수명을 제어하기 어려움. 채택하지 않음 |
| HTTP+decode 전체를 WPF 앱에 넣기 | 파일 수는 적지만 ViewModel에서 네트워크 구현을 분리하는 기존 참조 규칙에 불리함 |
| Application bytes 계약 + Infrastructure HTTP + WPF decode | 작은 계약 추가가 필요하나 독립 HTTP 테스트와 플랫폼 경계를 유지함. 채택 |
| metadata 서비스가 이미지까지 기다리기 | 조회 지연·이미지 실패가 metadata/Prepare를 묶음. 채택하지 않음 |

`VideoInfo`, parser 출력, `IVideoInfoService`, selector, Prepare/Execute 계약은 변경하지 않는다. CLI는 thumbnail 서비스 등록·이미지 다운로드를 하지 않아도 기존 info/clip 호출이 유지된다. parser fixture 기대값을 새 정책에 맞춰 느슨하게 바꾸지 않는다. 추가 HTTP 서비스는 parser를 거치지 않는 호출에도 자체 입력 검증을 수행한다.

### 4.2 URI·HTTP 정책 (Phase 3 결정)

기존 parser 허용 조건은 **공백·역슬래시 없음, well-formed 절대 HTTPS, DNS host, userinfo·문자 @·query·fragment 없음**이다. 이를 그대로 유지한다. query 제거, HTTP→HTTPS 보정, 다른 thumbnail URL 추측, `thumbnails[]` fallback은 하지 않는다. 거절된 URL을 다시 읽거나 fetch하지 않는다. query 포함 CDN을 표시하려면 이후 별도 parser 계약·fixture·개인정보/redirect 영향 검토가 필요하다. 이 Phase에서 완화하지 않는다.

HTTP 획득은 위 문법을 재검증하고 다음 **추가 제한**을 적용한다. parser의 URI 유효성과 실제 획득 허용성은 다른 계약이다.

| 항목 | 채택 정책 |
|---|---|
| 연결 | HTTPS 443만. 기본 TLS 인증서/hostname 검증 유지; 인증서 무시 callback 금지 |
| redirect | 자동 redirect 꺼짐, 3xx 전체를 `RedirectRejected`로 반환. Location을 추적하거나 UI/로그에 복제하지 않음 |
| 목적지 | localhost·단일 label·`.local`·`.localhost` 거절. DNS 결과 중 비공개/특수 주소가 하나라도 있으면 거절. 승인 IP에 직접 연결해 검증 후 다른 DNS 재해석으로 우회하지 않음 |
| DNS/IP 구현 | 전용 SocketsHttpHandler.ConnectCallback에서 취소 가능한 resolve→검사→검사한 IP로 connect. IPv4/IPv6·mapped IPv4 정규화. loopback/private/link-local/ULA/unspecified/multicast/예약·문서용 등 비전역 주소 차단표와 경계 fixture를 구현 단위에서 명시. TLS에는 원래 DNS host를 유지 |
| proxy / protocol | 전용 handler는 proxy·cookie·credentials 사용 안 함. HTTP/1.1 exact로 custom connect 경로를 고정. thumbnail 전용 제한이며 metadata/media HTTP 설정은 변경하지 않음 |
| 요청 | GET 한 번, 자동 retry 없음. 인증·쿠키·Referer·raw metadata 전달 없음. JPEG/PNG Accept. 웹 페이지·미디어 URI를 썸네일 대신 요청하지 않음 |
| 시간 | DNS·connect·TLS·headers·body 전체 10초의 linked deadline. body read에도 동일 token 전달. caller 취소와 timeout reason 구분 |
| 응답 | 200만 허용. MIME은 image/jpeg 또는 image/png, signature도 일치해야 함. 압축 Content-Encoding은 identity만, 자동 decompression 안 함 |
| 크기 | 압축 이미지 body 최대 4 MiB (4,194,304 bytes). Content-Length 초과 사전 거절; 길이 없거나 거짓이어도 streaming 누적 검사. 초과 탐지용 최대 1 byte 외 추가 body를 보관하지 않음 |
| 소유 | ResponseHeadersRead 후 제한된 buffer로 읽기. request·response·body stream은 service 내 using/finally로 반환 전에 종료. 디스크 임시 파일 없음 |
| 캐시 | 앱 디스크/URL 캐시 없음. 현재 metadata의 decoded image 하나만 보관. 같은 URL 재조회도 재요청하며 무기한 재시도 없음 |

이 추가 제한은 기존에 parser가 수락한 일부 주소(특수 포트·redirect·proxy 필수 네트워크 등)의 이미지 표시를 거절할 수 있다. metadata Ready와 클립 생성은 유지한다. CDN host allowlist는 실제 CHZZK host 근거가 없고 변경 추적이 필요하므로 채택하지 않는다. 대신 public 목적지 검증 비용을 수용한다. DNS 사전 검사만 하고 기본 connect가 다시 resolve하는 구현은 채택한 정책을 충족하지 않는다. 이 네트워크 정책의 구현·검증이 어려우면 해당 단위를 재검토하고, 기본 무제한 HTTP로 대체하지 않는다.

전용 실패 reason은 `UnsafeUri`, `DestinationRejected`, `RedirectRejected`, `TimedOut`, `HttpFailure`, `UnsupportedImage`, `TooLarge`, `InvalidImage`로 한정한다. UI는 안전한 한국어 문구로 표시한다. HTTP status 숫자는 보조 진단으로 허용하되 URI/path/query/Location/서버 본문·예외 message는 로깅하지 않는다. `image/webp`, SVG, GIF, HTML 등은 이번에는 표시하지 않는다. 지원 형식 확대는 실제 필요와 decode 비용을 별도 평가한다.

### 4.3 이미지 decode·메모리·취소 한계

bounded bytes에서 JPEG/PNG signature·헤더를 먼저 검사한다. PNG IHDR, JPEG SOF 탐색은 buffer 길이 안에서만 수행하고 잘린 데이터·overflow·상충 치수를 거절한다. 가로/세로 각각 4,096 이하, 총 8,000,000 pixel 이하, 양수 치수만 허용한다. decoder에 넘기기 전 제한을 적용하며 실제 decoded 치수와도 대조한다. PNG animation 확장 등 다중 프레임은 지원하지 않고 JPEG/PNG 정적 이미지 하나만 처리한다.

worker에서 stream 기반 BitmapImage를 생성한다. `CacheOption=OnLoad`, `UriSource` 미사용, aspect ratio를 유지하는 한 축의 DecodePixelWidth/Height를 지정해 최대 640×360 decode를 목표로 한다. resize나 DPI 변경마다 원본을 재획득·재decode하지 않는다. 성공하면 Freeze 후 UI에 전달하고 MemoryStream은 작업 내에서 dispose한다. Freeze 불가·decode 예외는 InvalidImage로 처리한다. UI가 원격 URI를 추가 로드하지 않게 한다.

4 MiB bytes와 원본 8 MP(32bpp 환산 약 30.5 MiB), 작은 최종 bitmap은 입력/픽셀 제한이지 전체 프로세스 메모리 상한이 아니다. WIC/native codec의 중간 메모리·CPU·해제 시점까지 이 값으로 보장하지 않는다. 이 정책은 별도 프로세스 격리도 아니다. 악성 codec 입력에서 하드 메모리/시간 격리가 필요해지면 별도 설계가 필요하다.

HTTP에는 10초 deadline을 적용하지만 동기 WPF decode에는 강제 중단 API가 있다고 가정하지 않는다. token은 decode 전후 검사하고 취소된 결과는 버린다. decode 시작 후 취소는 실제 반환까지 기다린다. Task timeout으로 관찰만 중단하거나 강제 thread 종료하지 않는다. UI는 worker 실행으로 계속 응답하고 종료는 pending decode를 끝까지 관찰한다. bitmap은 IDisposable로 취급하지 않으며 binding·VM 참조를 지워 GC 대상이 되게 한다. HTTP stream/CTS는 명시 dispose한다. GC.Collect를 호출하지 않는다.

API 근거: [BitmapImage.CacheOption](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.bitmapimage.cacheoption?view=windowsdesktop-10.0)은 stream 종료를 위해 OnLoad 사용을 설명한다. [BitmapImage](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.bitmapimage?view=windowsdesktop-10.0)의 decode 크기·stream API와 [ConnectCallback](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.connectcallback?view=net-10.0)의 연결 확장을 사용한다. [AllowAutoRedirect](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.allowautoredirect?view=net-10.0), [HttpCompletionOption](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-10.0)을 확인했다. 수치·주소 정책·오류 분류는 이 설계의 선택이며 API가 자동으로 보장하는 정책이 아니다.

검토 결론 B: URI 완화 없이 안전하게 표시를 시도하는 제한된 획득 파이프라인을 추가한다. 형식·redirect·proxy·크기 제한에 따른 표시 불가는 수용한다. 실제 양 사이트 URL/이미지의 적합성은 아직 미검증이며 8절에 별도 기록한다.

## 5. 논리 단위 C — 상태·재조회·취소·종료 수명

### 5.1 독립 상태와 작업 소유

기존 metadata `Empty/Loading/Ready/Failed`, job `Idle/Preparing/AwaitingConfirmation/Executing/Cancelling/Completed/Failed/Cancelled`, lifetime `Running/Closing/Closed`는 유지한다. `ThumbnailState = Empty/Unavailable/Loading/Ready/Failed/Cancelled`만 별도로 추가한다. Thumbnail Ready는 Metadata Ready와 같은 영상 epoch에만 가능하다.

썸네일 작업은 foreground `activeTask`에 합치거나 기존 `generation`을 증가시키지 않는다. metadata 성공 후 별도 `thumbnailEpoch`, `thumbnailCancellation`, `thumbnailTask`를 UI thread에서 등록한다. `IsBusy/CanEdit/CanPrepare`에는 thumbnail 로딩을 추가하지 않는다. Prepare/Execute의 동기 preamble은 계속 worker에서 실행한다. 이미지 작업도 worker delegate 안에서 service/decode를 호출한다.

썸네일 채택 조건은 `Lifetime==Running && MetadataState==Ready && metadataRevision==capturedRevision && thumbnailEpoch==capturedEpoch && 현재 slot identity 일치 && !token.IsCancellationRequested` 전부다. metadata snapshot/URI도 해당 slot에 고정한다. Start/End·Quality·Mode·Output 변경이나 Prepare 실행 때문에 이미지를 버리지 않는다. 이들은 input revision을 바꾸지만 동일 영상 이미지의 identity는 바꾸지 않기 때문이다.

현재 slot/task/CTS는 첫 await와 PropertyChanged 알림 전에 확보한다(TCS 기반 등록 등 기존 foreground 방식 준용). 이전 thumbnail을 취소하고 새 작업을 연결할 때 `previousTask`를 먼저 capture한다. 새 `thumbnailTask`는 previousTask의 실제 종료를 await한 뒤 현재 epoch/token을 재검사하고 HTTP/decode를 시작한다. 따라서 실작업은 동시에 하나이며 취소를 무시한 이전 decode가 있어도 새 decode를 병렬로 늘리지 않는다. 중간에 취소된 대기 slot은 fetch하지 않는다. 연결된 마지막 task가 앞선 작업의 관찰·정리까지 포함한다. 화면의 Loading은 이 제한 대기도 포함한다.

각 작업의 finally는 자신의 CTS·buffer/stream만 정리한다. 오래된 finally가 새 slot/state/image/CTS를 null로 만들지 않게 identity를 검사한다. 예외·취소도 항상 task에서 관찰하며 fire-and-forget 네트워크나 async void 서비스는 만들지 않는다. 취소 요청자는 CTS.Dispose를 하지 않고 해당 작업 owner가 실제 완료 후 dispose한다.

### 5.2 사건별 처리

| 사건 | metadata / 선택 / 준비 | thumbnail / 표시 |
|---|---|---|
| 최초·URL 변경 | metadata·selection 비움, metadataRevision 증가, Quality 목록 Best만, 기존 InputChanged로 준비 무효화 | epoch 증가, 이전 CTS 취소, image/필드/진단 즉시 비움, Empty. 이전 task는 계속 추적 |
| 동일 URL 재조회 포함 Load 시작 | 이전 metadata/선택/준비/필드 전부 먼저 제거; URL 검증 실패도 이전 정보 복원 안 함 | 이전 이미지 즉시 제거·취소. 성공 전 fetch 없음 |
| 조회 Loading | 기존 입력 잠금·중복 Load 차단, Cancel 제공 | Empty placeholder. 이전 이미지 표시 안 함 |
| 현재 조회 성공 | Completed·양수 Duration gate 후 snapshot 반영, Start/End=0, Quality=Best, Output/Mode 보존, 검증·projection을 먼저 일치시킨 뒤 Ready 공개 | URI null이면 Unavailable; 아니면 slot 등록·Loading. metadata foreground 완료를 이미지 대기와 묶지 않음 |
| 조회 실패 / invalid state/duration | Failed, 이전 metadata·선택·준비 없음 | Empty. 이미지 획득 안 함 |
| 조회 취소 요청 | 요청만으로 unlock하지 않고 foreground 실제 종료 대기. 이후 Empty | 이전 이미지 복원 없음. 늦은 조회 성공이어도 이미지 시작 금지 |
| 이미지 성공/실패 | metadata Ready·선택·Prepare 조건 유지, input/metadata revision 변경 금지 | 채택 guard 통과 시 Ready 또는 Failed. 실패 이유와 전체 `영상 불러오기`로 재시도 안내 |
| 이미지 취소 | metadata Ready·선택 유지 | 전용 `썸네일 로딩 취소` 명령. 즉시 token 요청, `취소 요청 · 이미지 작업 종료 대기` 표시. 실제 작업 반환 후 Cancelled. token 요청 후 늦은 성공 버림 |
| Quality 변경 | 입력 숫자 보존, input revision 증가·준비 무효화·선택 FPS/해상도 projection·기존 검증 갱신 | 동일 영상 이미지/작업 유지 |
| Prepare/확인/Execute | 기존 상태·guard 유지. 이미지가 늦게 와도 PreparedClip·요약·결과를 수정하지 않음 | 동일 metadata epoch 이미지 반영만 가능, 메인 StatusText/ResultText 덮지 않음 |
| 첫 Close | Closing, 준비 폐기, 새 명령 차단, 기존 foreground/path 취소·대기 | epoch 증가·slot 취소·image 참조 제거. 연결된 thumbnailTask의 실제 완료까지 대기 |
| 반복 Close / 경고 확인 | 같은 shutdownTask·Close latch 유지 | 이미지 대기도 생략 불가. 모든 task 완료 전 경고 확인 버튼 활성화 금지 |

별도 이미지 재시도 버튼은 추가하지 않는다. 실패/취소 후 `영상 불러오기`가 metadata와 이미지를 함께 새로 얻는 일관된 경로다. 재조회는 Start/End·Quality 초기화와 준비 무효화를 수반한다고 안내한다. 자동 이미지 retry·background metadata refresh는 없다. 기존 Cancel 버튼은 foreground 작업만 취소한다. 이미지 전용 취소는 Loading이면서 미요청이고 Running일 때만 가능하며 clip 취소와 섞지 않는다.

### 5.3 오래된 응답과 진단

metadata 성공/실패/catch/finally의 표시 갱신에도 generation·input revision·수명 guard를 적용한다. 특히 기존 `RecordOperationDetails`가 채택 guard 전 호출되는 부분을 구분한다. **자원 경고/ProcessTerminationFailed의 안전상 관찰·누적은 항상 수행**하고, 과거 요청의 일반 Status/OperationDetails/metadata/이미지 표시만 막는다. stale 결과를 전부 무시하면서 잔존 자원 경고까지 잃지 않는다. thumbnail에는 clip 세션 잠금을 적용하지 않는다.

조회 Ready 통지 전에 projection·선택을 한 snapshot으로 완성한다. 알림 중 재진입도 기존 `activeTask` guard가 막아야 한다. 오래된 metadata catch/finally와 thumbnail finally가 새 화면을 Empty/Failed로 덮는 경합을 제어 가능한 fake로 검증한다. 공개 속성에 대한 프로그램상 URL 변경도 UI disabled 상태만 믿지 않고 revision으로 방어한다.

### 5.4 종료 순서와 불변 계약

`ShutdownAsync`는 Closing을 먼저 확보하고 foreground와 thumbnail 취소를 요청한 뒤 `activeTask`, 기존 누적 `pathTask`, 연결된 마지막 `thumbnailTask`를 await한다. Closing 이후 신규 slot을 등록할 수 없으므로 기다릴 task 집합이 빠지지 않는다. `CanAcknowledgeClose`도 thumbnailTask 완료 조건을 포함한다. 지연 decode를 timeout으로 버려 DI를 해제하지 않는다.

작업·stream·CTS 정리 → UI 이미지 참조 제거 → 기존 Dispatcher 지연 Close → DI dispose(전용 HttpClient 포함) → 종료 로그 → logger → SelfLog 순서를 유지한다. MainWindow Closing 안에서 동기 완료 후 재귀 Close하지 않는다. 예상 밖 thumbnail 실패는 관찰·안전한 진단 후 해당 표시만 실패시키며 clip 최종 결과·잔존 자원 경고를 덮지 않는다. OS 강제 종료 정상 정리는 보장하지 않는다.

반드시 유지할 회귀 계약:

- URL·Start·End·Quality·Mode·Output 변경, 재조회·돌아가기·종료가 준비를 무효화한다. 이미지 로딩/완료는 입력 변경이 아니다.
- PreparedClip은 같은 객체를 한 번만 Execute한다. 재실행은 새 Prepare다.
- 성공은 최종 ClipJobResult 기준이다. progress Completed/100%·이미지 Ready·파일 존재로 성공 처리하지 않는다.
- FPS null은 0/30/대표값으로 대체하지 않는다. 기존 `Prepare.Range=null`·FF 상한/구간 순서/길이 pending 검사를 유지한다. 표시 목적으로 probe/미디어 획득을 새로 시작하지 않는다.
- 원본 획득 64 MiB/60초, playlist 2 MiB·계획 segment 64개, codec/clock·SourceEndVerified=false 및 CHZZK/먼 위치 Accurate 경계를 보존한다. 이미지 4 MiB/10초와 원본 예산의 의미를 혼용하지 않는다.

검토 결론 C: 독립 이미지 작업은 Prepare를 지연시키지 않지만 종료 대기 대상은 늘린다. foreground와 같은 generation을 재사용하면 Prepare가 이미지를 무효화하거나 이미지가 clip 상태를 바꿀 수 있어 별도 epoch를 채택한다. 전체를 단일 busy 작업으로 묶는 대안은 단순하지만 이미지 지연으로 클립 준비까지 막으므로 채택하지 않는다.

## 6. 논리 단위 D — 후속 구현 순서·보고·커밋

이번 요청에서는 다음 단위를 실행하지 않는다. 후속 구현이 명시되면 각 단위를 **구현 → 관련 검증 → 결과/제약 보고 → 해당 단위만 커밋** 순서로 완료한다. 실패/미확인을 성공으로 숨기거나 마지막에 커밋을 몰아서 소급 기록하지 않는다. 관련 문서 결과도 해당 시점에 기록한다. 사용자 변경은 선택적으로 보존하고 전체 staging을 하지 않는다.

| 순서 / 논리 커밋 | 범위 | 단위 종료 검증 |
|---|---|---|
| 0 Git 준비 | branch·HEAD·tag·사용자 diff 재확인, 구현 branch/Base 확정 및 Baseline 반영 | Completion 이후 제품 diff·설계와 실제 계약 재대조. 이 단계만으로 Phase 완료 tag 생성 금지 |
| 1 상세 텍스트·선택 projection | MetadataPresentation/helper, MainViewModel metadata partial, 여섯 필드 중 이미지 placeholder·XAML 접근성 | projection/입력 fake·STA binding, 기존 Input/Preparation 회귀, WPF build. 아직 HTTP 호출 없음 |
| 2 제한 HTTP 계약·구현 | Application thumbnail 결과/계약, Infrastructure HTTP·주소 정책, fake handler/연결 테스트 | URI/parser 회귀·주소 경계·redirect/시간/길이/stream dispose·CLI 회귀, 솔루션 build. UI 미연결 가능 |
| 3 decode·이미지 수명 통합 | WPF decoder, 별도 slot/CTS/task/epoch·취소, DI·shutdown/ack, thumbnail binding | decode 제한·STA/Dispatcher·실제 Window 반복 Close, 기존 Prepare/Execute/종료 전체, 솔루션 build |
| 4 실제 UI·최종 검증·기록 | 양 사이트 metadata/원격 thumbnail, 장문/DPI·취소/경합·종료, regression·증거 | 7절. 실패 보완은 해당 변경 후 필요한 검증 재수행·별도 논리 커밋. 필수 검증 전 Completion/Passed 금지 |

단위 2의 public 목적지 차단과 custom connect는 새 HTTP 동작의 필수 경계다. 단위 3을 연결하기 전에 완료한다. 각 중간 커밋은 build 가능한 범위로 구성하고 새 계약·사용자 코드를 미스테이징 구현에 의존시켜 검증하지 않는다. 패키지 설치는 계획하지 않으며 새 필요가 생기면 이유와 영향을 먼저 기록한다.

## 7. 완료 조건과 검증 계획 (아직 실행하지 않음)

### 7.1 결정론적 테스트

| 검증 영역 | 반드시 확인할 사례 / 통과 기준 |
|---|---|
| 표시 | 여섯 label, Title/Uploader null·공백·장문, 30시간·소수 Duration, issue 없는 누락·Rounded·format index 구분 |
| FPS/해상도 | 서로 다른 FPS의 동일 높이·중복 높이·오디오 전용·Unknown video·높이 없음, Best/높이 목록 일치. 30000/1001·60000/1001 exact 표시와 null 경로 |
| 선택·준비 | Quality 변경 시 숫자 유지·FPS 재검증·준비 무효화. metadata Ready+선택 실패에서 Prepare 불가. image Loading/Failed/Cancelled는 유효 Prepare를 막지 않음 |
| parser 계약 | query·fragment·userinfo/@·공백·역슬래시·HTTP·IP literal 기존 거절 유지. 원본 field/fixture를 완화해 표시를 통과시키지 않음 |
| HTTP URI/연결 | invalid URI 및 443 외 포트·로컬 host의 무요청 거절. IPv4/IPv6/mapped 특수 주소·mixed DNS 결과 거절, 검증한 IP만 connect, TLS host 유지·인증서 오류 실패 |
| HTTP 응답 | 정상 JPEG/PNG, 3xx/4xx/5xx, MIME/signature 불일치·HTML/WebP, Content-Encoding, 빈 body, 거짓/없는/초과 Content-Length, chunked 4 MiB와 +1 byte 경계, 느린 header/body·DNS/연결 취소·timeout |
| 정리 | 정상/예외/초과/취소 모두 request/response/stream/socket dispose. 검증 실패 후 fallback 요청/재시도 없음. 로그에 sentinel URL/token/body/예외 내용 없음 |
| decode | 작은 JPEG/PNG·큰 정상 이미지, truncated/손상·헤더 치수 overflow·4096 경계·8 MP 초과·animation, frozen 결과·stream 해제 후 접근·출력 크기/aspect·decode 실패 |
| stale | A 조회/이미지 지연→URL B·동일 URL 재조회·실패·취소→A 완료. 이전 성공/오류/finally가 현재 필드/이미지/상태를 덮지 않음. 자원 경고는 보존 |
| 수명 | 동기 service/decode preamble worker 실행, token 무시 fake·지연 decode 후 새 fetch 직렬화, 모든 예외 관찰, 반복 Shutdown 같은 task, 실제 Window.Close 2회에도 이미지 완료 전 Close/DI 해제 없음 |
| 실행 회귀 | PreparedClip 동일 객체 1회·모든 입력 무효화·이미지 완료 후 확인 유지, progress Completed 후 최종 실패·Finalizing 후 성공, FPS null pending 경로, session 잠금·잔존 경고 유지 |

HTTP 정책은 네트워크 없이 fake handler·resolver/connector seam으로 검증한다. 실제 연결 제어는 필요한 소규모 Infrastructure 내부 seam만 만들고 새 범용 네트워킹 프레임워크를 만들지 않는다. 실제 socket/TLS 경로도 제어 가능한 테스트 endpoint로 별도 확인하며 테스트 설정으로 production 목적지 gate를 전역 비활성화하지 않는다. 기존 Core 계산 테스트를 구현과 동일한 UI 테스트로 중복하지 않고 새 연결·경계·경합을 검증한다.

### 7.2 실제 WPF·원격 관찰

후속 구현 후 지원되는 YouTube/CHZZK 완료 영상 각각 Load를 실제 WPF에서 실행한다. 도구 버전·OS/DPI·소스 ID·선택 포맷·FPS rational·해상도·누락 필드·이미지 결과/reason·관찰 시간을 안전한 기록에 남긴다. signed URL·raw JSON·thumbnail body를 Git 증거에 넣지 않는다.

양 사이트 모두 여섯 필드의 정상/누락 의미를 확인한다. parser에서 허용되고 JPEG/PNG·직접 HTTPS를 만족하는 실제 원격 이미지의 표시 성공도 확인해야 한다. 선택한 소스가 정책에 걸리면 거절 동작만 통과한 것으로 기록하고, 이를 정상 이미지 표시 성공으로 세지 않는다. 양 사이트에서 현 정책에 맞는 사례를 찾지 못하면 수용성 미해결로 남겨 완료 판단 전에 검토한다. CHZZK metadata 실패 사례는 초기화/재조회 안내와 기존 지원 경계로 분리한다.

실제 이미지 로딩 중 창 이동·재그리기·Prepare 가능 여부, 전용 이미지 취소, URL 변경/동일 URL 재조회, 실패 후 이전 필드·이미지 제거, 최소 크기·스크롤·복사·키보드·DPI를 확인한다. 네트워크로 재현하기 어려운 느린 응답·실패·decode 지연은 실제 Window+제어 fake 관찰과 원격 관찰을 나눠 기록한다. 반복 Close·stream 정리·DI/logger 순서도 fake/Dispatcher 증거와 실제 종료 로그·연결/프로세스 관찰을 구분한다. WPF/native 메모리가 매번 즉시 OS로 반환된다고 주장하지 않는다.

### 7.3 회귀 명령과 미디어 재검증 기준

예정 명령은 `dotnet build ClipMaker.slnx --no-restore --nologo`와 아래 4개 csproj 각각의 `dotnet test <csproj> --no-build --no-restore --nologo`다. 필요 restore·환경 실패는 실제 결과와 조치를 기록하고 임의 패키지 업그레이드/보안 설정 변경을 하지 않는다.

- `tests/ClipMaker.Media.Core.Tests/ClipMaker.Media.Core.Tests.csproj`
- `tests/ClipMaker.Media.Infrastructure.Tests/ClipMaker.Media.Infrastructure.Tests.csproj`
- `tests/ClipDownloader.Infrastructure.Tests/ClipDownloader.Infrastructure.Tests.csproj`
- `tests/ClipDownloader.Tests/ClipDownloader.Tests.csproj`

기존 1,230개는 회귀 기준이며 신규 테스트 합계는 실행 결과로 기록한다. build/test 성공만으로 실제 WPF·remote thumbnail 완료를 선언하지 않는다. CLI metadata/diagnose smoke와 thumbnail 자동 획득이 추가되지 않았음도 확인한다.

기존 미디어 bytes·선택·예산·검증 gate를 변경하지 않는 Phase 3에서는 Phase 2의 실제 네 클립 증거를 유지하고, 새 PreparedClip/Execute 회귀와 실제 WPF 준비/확인 연결을 검증한다. 이 경우 네 클립을 새로 생성했다고 주장하지 않는다. 구현이 selector·요청/FPS 변환·Prepare/Execute·media/source DI·예산·종료 정리 계약까지 변경하면 영향 분석 후 양 사이트·양 모드 실제 생성·decode·사용자 A/V 확인과 실제 취소/자식 종료 검증을 다시 수행한다. 이번 설계 중에는 어떤 미디어 다운로드도 실행하지 않는다.

검토 결론 D: 표시·HTTP·decode/수명·실제 검증을 독립 커밋 경계로 나누고 회귀 및 실제 관찰을 모두 완료 조건에 포함한다. 후속 구현을 요청받기 전에는 예정 검증을 실행 결과로 바꾸지 않는다.

## 8. 미해결 사항·차단 여부·인계

| 항목 | 사실 / 아직 미확인 | 영향·처리 시점 |
|---|---|---|
| Phase 3 branch/Base | branch는 사용자 지정 `phase/3`으로 확정, 실제 checkout도 일치. Base 후보는 설계 snapshot HEAD | 설계 비차단. 제품 구현 전 6절 단위 0에서 실제 변경 대조 후 Base 확정·Baseline 반영 |
| 양 사이트 thumbnail 적합성 | 현재 parser/remote URI·형식이 채택 정책을 얼마나 만족하는지 이번에는 획득하지 않음 | 설계 진행 비차단, Phase 3 완료 판단 전 7.2 실제 검증 필수. query/redirect/형식 완화는 별도 변경 기록 필요 |
| 이미지 4 MiB/10초·4096/8 MP·640×360 | 설계 한도이며 실제 이미지 분포·메모리 측정 근거는 아직 없음 | 단위 3/4에서 경계·정상 이미지 수용성 확인. 실패 시 근거를 남겨 수치만 재검토; 원본 미디어 예산은 변경하지 않음 |
| custom connect 주소 분류·TLS | .NET API 확인, production 구현과 차단표/경계 테스트는 아직 없음 | 단위 2 완료 전 구체화·검증 필수. 구현 연결 차단 조건이며 설계 문서를 작성하는 차단은 아님 |
| 동기 decode 강제 중단/하드 메모리 상한 | 현재 선택한 WPF in-process 경로로 보장하지 않음 | 제한 입력·worker·직렬화·실제 반환 대기 채택. 더 강한 보장이 필요하면 구현 전 해당 요구 재설계 |
| CHZZK MPD/먼 위치 Accurate | 기존 지원 경계, 해결되지 않음 | 별도 미디어 지원 작업. UI/thumbnail 성공으로 해결했다고 표시하지 않음 |

다음 Phase는 MetadataPresentation이 UI projection일 뿐 source/selection/PreparedClip 계약이 아니라는 점을 유지한다. 이미지 성공/실패는 clip 실행 상태와 독립이고, 입력 무효화·한 번 실행·최종 반환 성공·정리 후 DI/logger 종료가 계속 권위 있는 계약이다. Queue/Player 설계는 이 문서에서 확정하지 않는다.

## 9. 이번 설계의 검증·변경 기록

- 2026-10-15: Git 시작 상태·Completion/tag·제품 diff와 연결 문서·실제 metadata/selection/input/job/DI/종료/테스트 소스를 읽고 논리 단위 A~D를 검토했다. 설계 선택과 API 근거·실제 미검증 항목을 구분했다.
- 이 파일만 추가한다. Baseline의 Phase 2 Completed/Passed·완료 태그·기존 기록은 변경하지 않는다. 설계 작성으로 Phase 3 구현 완료/Passed를 부여하지 않는다.
- 제품 코드 구현, 패키지 설치, build/test 재실행, WPF 실행, metadata/thumbnail/미디어 다운로드는 이번 작업에서 수행하지 않았다.
- 실제 문서 검사: 상대 링크 14개와 기존 연결 코드·테스트 경로 17개 모두 존재 확인. `git diff --check` 및 untracked 설계 파일의 `git diff --no-index --check`에서 공백 오류 없음. 최초 검사에는 LF→CRLF 정규화 안내가 있었으며 저장소 작업 파일 관례에 맞춰 CRLF로 정리했다. 최종 status는 이 설계 파일 하나의 추가뿐이며 branch/HEAD와 기존 tracked 파일은 그대로다. 브랜치 생성·commit·tag 변경·push는 하지 않았다.
- 2026-10-15 사용자 후속 결정: Phase 3은 `phase/3` 브랜치를 사용한다. 재확인 시 이미 해당 브랜치였고 HEAD는 `5d5a06e6a6d0b8ec8ea952e738ecf99756a53712`로 동일했다. 최초 조사 표의 `phase/2`는 당시 관찰 기록으로 유지한다. 나머지 미확인 항목은 구현 단위별 구체화·검증 대상으로 유지하며 당장 추가 사용자 결정을 요구하지 않는다.

## 10. 구현 후 결과·현재 인계 (2026-10-16)

- 사용자 구현 요청에 따라 6절 단위별 구현·검증·결과 기록·커밋을 진행했다. Base는 `5d5a06e6a6d0b8ec8ea952e738ecf99756a53712`, branch는 `phase/3`이다. 완료점은 [Baseline](../PHASE_BASELINE.md)을 따른다.
- 7절 필수 검증 완료: build 경고/오류 0, 1,353개 통과, 양 사이트 실제 metadata/직접 HTTPS thumbnail/WPF 표시, native 100%·200% 및 원래 150% 복원, 최소 Window·복사·키보드·스크롤, 입력/선택 무효화·준비·종료. 지연·취소·stale·반복 Close는 제어 fake/실제 STA Window 근거와 원격 관찰을 구분했다. 상세 시간·실패 이력·제약은 [구현 기록](PHASE_3_IMPLEMENTATION.md), 안전한 값은 [증거 JSON](evidence/PHASE_3_METADATA.json)에 있다.
- 8절의 URI 수용성·주소 분류·TLS·치수/bytes 경계는 실제 양 사이트 성공 및 경계 테스트로 확인했다. 전 세계 CDN 수용성·강제 decode 중단·하드 메모리 상한을 보장하지 않는다. query/redirect/기존 parser 정책과 미디어 예산을 완화하지 않았다.
- 주소 차단표·PNG CRC preflight·알림 재진입 guard는 설계 경계를 구체화했다. 공유 WPF 자원 경합 가설에 따라 테스트 어셈블리를 직렬화했으며 그 실패와 trade-off도 기록했다. 제품 요구/지원 범위 변경이나 새 패키지 없음.
- 7.3에 따라 Phase 2 네 클립/A-V 증거를 유지했다. CHZZK MPD·먼 위치 Accurate·codec/clock 등 기존 경계는 후속 작업에서도 그대로 적용한다. Queue/Player/ClipEditor는 구현하지 않았다.
- 최초 사용자 원문은 `.artifacts/phase3-verification/inputs`에 SHA256과 함께 보존했다. 이 갱신은 역사적 계획을 실제 수행 결과로 소급하지 않고 현재 결과를 연결한다.
