# ClipDownloader 프로젝트 구조 설계

상태: 요구사항 분석 및 구조 설계안. 프로젝트 생성·이동·구현은 아직 수행하지 않았다.

## 1. 근거와 현재 상태

분석 대상은 `docs/ClipDownloader/requirements`의 다음 5개 문서다.

- `01_PRODUCT_REQUIREMENTS.md`: 제품 범위와 사용자 시나리오
- `02_ARCHITECTURE.md`: 계층, 모델, 외부 도구 및 테스트 원칙
- `03_IMPLEMENTATION_PLAN.md`: Phase 0~8과 완료 조건
- `README_FOR_CODEX.md`: 문서 적용 순서와 단계별 작업 지침
- `README_FOR_REPOSITORY.md`: 단일 저장소, 독립 앱, 필요한 공유 라이브러리

현재 `ClipMaker.slnx`는 `ClipDownloader/ClipDownloader.csproj` 하나를 참조한다. 해당 프로젝트는 `net10.0-windows` WPF 기본 템플릿이며, 메인 창은 비어 있다. 다운로드·미디어 처리·MVVM 구현은 없다. 솔루션, 앱, 요구사항 폴더는 확인 시점에 Git 미추적 상태다.

설계의 기준은 **단일 저장소 + 독립 실행 앱 + UI와 분리된 미디어 처리**다. 최신 `README_FOR_REPOSITORY.md`와 사용자 지시에 따라 저장소·솔루션은 **ClipMaker**, 이번 설계·구현 대상 앱은 **ClipDownloader**, 향후 별도 앱은 **ClipEditor**, 공유 미디어 모듈 계열은 **ClipMaker.Media**로 확정한다.

이번 개발 범위는 ClipDownloader다. ClipEditor는 참조 경계를 설명하기 위한 향후 대상이며, 프로젝트 생성·편집기 기능·편집기 전용 API 설계는 현재 범위에 포함하지 않는다. 기존 솔루션 형식인 `ClipMaker.slnx`를 유지한다. 저장소 문서의 `ClipMaker.sln`은 동일한 솔루션을 가리키는 구조 예시로 해석하며 형식 변환은 필요하지 않다.

명세의 앱 이름, 실행 파일 예시, 브라우저 표시 문구는 ClipDownloader로 통일한다. 향후 URI scheme 예시는 `clipdownloader://open?url=...`을 사용한다. 프로토콜 등록 자체는 Phase 6의 구현 범위다.

## 2. 요구사항 해석과 범위

| 요구사항 | 구조에 반영할 사항 | 구현 시점 |
|---|---|---|
| CHZZK VOD, YouTube URL | 공식 지원 URL 판별과 yt-dlp 어댑터 | Phase 1 |
| 영상 정보·화질 조회 | 원시 포맷과 UI 화질 선택 항목 분리 | Phase 1~3 |
| HH:MM:SS:FF | 공유 미디어 계층의 타임코드 값 객체 | Phase 1 |
| Fast / Accurate, MP4 | 공유 FFmpeg 처리와 다운로드 조정 분리 | Phase 1~2 |
| 비동기, 진행률, 취소 | Application 계약과 공통 프로세스 실행 | Phase 1~2 |
| 여러 구간 | 동일한 단일 작업을 순차 실행하는 큐 | Phase 4 |
| Preview / 브라우저 연동 | 앱 기능으로 추가, 핵심 모델과 분리 | Phase 5~6 |
| 로컬 영상 | 같은 미디어 처리 계약에 로컬 입력 연결 | Phase 7 |
| 편집기 재사용 | 편집기와 다운로더 간 직접 참조 금지 | Phase 8 및 이후 |

온라인 구간 추출이 초기 MVP의 중심이다. 로컬 영상은 제품의 최종 범위에 포함되지만, 구현 계획에 따라 실제 UI 지원은 Phase 7에서 추가한다. 공유 모델은 처음부터 온라인 URL에 종속시키지 않는다.

Phase 2에서도 화질 선택과 타임코드 검증에 필요한 최소 메타데이터는 사용한다. Phase 3은 썸네일 등 상세 정보 표시를 확장하는 단계로 해석한다.

## 3. 목표 디렉터리 구조

아래는 향후 구현 시 적용할 구조이며, 이번 설계 작업에서 디렉터리별 프로젝트를 생성하지 않는다.

```text
ClipMaker/
├─ ClipMaker.slnx
├─ apps/
│  ├─ ClipDownloader/                  # 기존 WPF 프로젝트를 이후 이동
│  │  ├─ Composition/                 # DI 등록, 시작·종료 처리
│  │  ├─ Views/
│  │  ├─ ViewModels/
│  │  ├─ Commands/
│  │  ├─ Dialogs/                     # 파일·폴더 선택 등 WPF 기능
│  │  ├─ Resources/
│  │  ├─ App.xaml
│  │  └─ ClipDownloader.csproj
│  └─ ClipDownloader.Cli/              # Phase 1 검증용 호스트
├─ src/
│  ├─ ClipDownloader.Application/
│  │  ├─ UseCases/                    # 메타데이터 조회, 클립 생성
│  │  ├─ Models/                      # VideoInfo, ClipJob, 화질 선택
│  │  ├─ Contracts/                   # 다운로드·설정·출력 관리 계약
│  │  ├─ Validation/
│  │  └─ Jobs/                        # Phase 4에서 큐 처리 추가
│  ├─ ClipDownloader.Infrastructure/
│  │  ├─ Sources/                     # URL 판별, yt-dlp source 연결
│  │  ├─ YtDlp/                       # 명령 인자, JSON 변환, 진행률
│  │  ├─ Downloads/                   # 원격 구간 획득 전략
│  │  ├─ FileSystem/                  # 출력 예약, 임시 작업 폴더
│  │  └─ Configuration/               # 설정 저장, 도구 경로 설정
│  ├─ ClipMaker.Media.Core/
│  │  ├─ ValueObjects/                # FrameRate, Timecode, MediaTimeRange
│  │  ├─ Models/                      # ClipSegment, MediaInput, MediaInfo
│  │  └─ Contracts/                   # 미디어 분석·처리 계약
│  └─ ClipMaker.Media.Infrastructure/
│     ├─ FFmpeg/                      # trim, mux, remux, encode
│     ├─ FFprobe/                     # 결과 검사, 로컬 미디어 분석
│     ├─ ProcessExecution/            # 공통 비동기 프로세스 실행
│     └─ Tools/                       # 실행 파일 탐색·검증
├─ tests/
│  ├─ ClipMaker.Media.Core.Tests/
│  ├─ ClipMaker.Media.Infrastructure.Tests/
│  ├─ ClipDownloader.Application.Tests/
│  ├─ ClipDownloader.Infrastructure.Tests/
│  └─ Fixtures/                       # 익명화한 yt-dlp JSON 등
└─ docs/
   └─ ClipDownloader/
      ├─ PROJECT_STRUCTURE_DESIGN.md
      ├─ PHASE_DESIGN.md
      ├─ requirements/                # 제품·아키텍처·단계·저장소 명세
      └─ phases/                      # 현재 Phase 상세 설계·체크리스트
```

초기에는 ClipDownloader WPF 앱과 라이브러리 4개로 경계를 만든다. CLI는 Phase 1에서 추가한다. ClipEditor 프로젝트는 별도 개발 요청 시 추가하며, 플레이어 라이브러리·브라우저 확장·독립 캐시 프로젝트는 해당 단계까지 생성하지 않는다. 표시한 하위 폴더도 실제 파일이 생길 때 만든다.

최신 저장소 문서의 `ClipMaker.Media`는 공유 미디어 모듈의 이름이다. 이 설계에서는 도메인과 도구 구현의 의존성을 구분하기 위해 `ClipMaker.Media.Core`와 `ClipMaker.Media.Infrastructure` 두 라이브러리로 구체화한다. 두 라이브러리는 현재 다운로더에 필요한 시간·구간·FFmpeg 기능만 제공하며, 편집기의 Timeline·Effect·Keyframe 기능은 포함하지 않는다. 미래 사용만을 이유로 추가 공유 프로젝트를 만들지 않는다.

## 4. 프로젝트 책임과 참조 규칙

| 프로젝트 | 책임 | 직접 참조 가능한 프로젝트 |
|---|---|---|
| ClipMaker.Media.Core | 플랫폼과 UI에 독립적인 시간·구간·미디어 계약 | 없음 |
| ClipMaker.Media.Infrastructure | FFmpeg/ffprobe, 프로세스 실행, 도구 탐색 | Media.Core |
| ClipDownloader.Application | 사용자 작업 조정, 검증, 작업 결과·진행률 | Media.Core |
| ClipDownloader.Infrastructure | 사이트 판별, yt-dlp, 원격 입력 획득, 설정·출력 관리 | Downloader.Application, Media.Core, Media.Infrastructure |
| ClipDownloader | WPF 화면과 앱 구성 | Application, Media.Core, 두 Infrastructure |
| ClipDownloader.Cli | 같은 유스케이스를 호출하는 검증용 CLI | Application, Media.Core, 두 Infrastructure |

WPF 앱의 Infrastructure 참조는 Composition에서 구현체를 연결하는 용도로 제한한다. ViewModel은 Application 계약만 사용하며 yt-dlp JSON, FFmpeg 인자, Process 타입을 알지 않는다.

공유 라이브러리는 일반 `net10.0`, WPF 앱은 기존 `net10.0-windows`를 기준으로 계획한다. 실제 지원 OS는 Windows이며, 일반 TFM 자체를 다른 OS의 동작 보장으로 해석하지 않는다.

`02_ARCHITECTURE.md`의 Core / Application / Infrastructure 경계를 유지하되, `README_FOR_REPOSITORY.md`에 맞춰 Core를 공유 미디어 도메인으로 구체화한다. 다운로더 전용 모델은 Application에 둔다. 별도 Downloader.Core는 초기 규모에서 만들지 않는다.

Media.Infrastructure에 공통 프로세스 실행을 두어 yt-dlp와 FFmpeg가 중복 구현하지 않도록 한다. 이 계층은 다운로더 프로젝트를 참조하지 않는다. 프로세스 실행만을 위한 별도 범용 라이브러리는 만들지 않는다.

향후 ClipEditor는 필요한 Media 라이브러리만 참조한다. ClipDownloader와 ClipEditor는 실행 앱뿐 아니라 각 앱 전용 Application·Infrastructure 프로젝트도 서로 참조하지 않는다. CHZZK/YouTube 조회·다운로드 로직은 ClipDownloader 전용 계층에 유지한다.

편집기에 온라인 영상 전달이 필요해지면 우선 ClipDownloader가 생성한 로컬 파일을 편집기의 미디어 입력으로 받는 경계를 사용한다. 실제 양쪽에서 필요한 일반 미디어 기능만 ClipMaker.Media에 추가한다. 사이트별 온라인 import를 공유하려면 저장소 정책을 별도로 검토해야 하며, 현재 설계에서는 Downloader.Application 직접 참조를 해결책으로 사용하지 않는다.

## 5. 핵심 모델과 계약

### 공유 미디어 모델

- **FrameRate**: 분자/분모로 표현하는 유리수. 30/1, 60/1, 30000/1001 등을 보존한다. 원시 도구가 소수 FPS만 제공할 때 표준 값으로 보정할 조건은 Phase 1에서 검증한다.
- **Timecode**: 시·분·초·프레임과 FrameRate를 갖는 불변 값 객체. 파싱, 표시, 범위 검증, 시간·프레임 변환을 담당한다.
- **MediaTimeRange**: 미디어 시간축의 시작과 종료. 내부 계산에서 부동소수점 누적을 피하고 CLI 경계에서만 초 문자열로 변환한다.
- **ClipSegment**: 사용자 구간 ID, Start, End, Name. UI 선택 상태나 다운로드 상태는 포함하지 않는다.
- **MediaInput**: 미디어 처리에 전달하는 입력과 그 입력의 시간축 정보. 초기에는 획득한 임시 파일을 표현한다. 사이트 판별이나 로그인 정보를 갖지 않는다.
- **MediaProcessingRequest / Result**: 입력, 추출 범위, 모드, 출력 정책 및 실제 생성 파일·검사 결과.

### 다운로더 모델

- **VideoSourceReference**: 정규화 URL, 플랫폼, 영상 식별자.
- **VideoInfo / VideoFormat**: 제목·길이·업로더 및 원시 포맷 정보. FPS는 포맷별로 다를 수 있다.
- **QualityOption / FormatSelection**: Best 또는 실제 제공 해상도 선택. 단일 format ID뿐 아니라 video/audio 조합을 표현한다.
- **ClipJob**: 작업 ID, 소스, 구간, 선택 화질·FPS, 모드, 출력 정책을 실행 시점에 확정한 불변 작업.
- **AcquiredMedia**: 획득 파일, 포함한 원본 구간, 로컬 시간축과 원본 시간축의 관계, 처리 필요 사항.
- **JobProgress / ClipJobResult**: 단계, 선택적 진행률, 현재 구간, 완료 경로 또는 구조화된 오류.

요구사항의 `SourceUrl` 기반 ClipJob은 다운로더 전용으로 유지한다. 공유 Clip Processor는 ClipJob 전체 대신 MediaInput과 MediaTimeRange를 받는다. 이 경계가 로컬 파일 지원과 편집기 재사용의 기반이다.

### 계약 배치

| 계약 | 정의 위치 | 구현 위치 / 역할 |
|---|---|---|
| IVideoSource | Downloader.Application | Downloader.Infrastructure: CanHandle, 메타데이터 조회 |
| IClipDownloadService | Downloader.Application | Downloader.Infrastructure: 원격 구간 획득 |
| IOutputWorkspace | Downloader.Application | Downloader.Infrastructure: 출력 예약·임시 파일 수명 |
| IMediaProcessingService | Media.Core | Media.Infrastructure: trim·mux·재인코딩 |
| IMediaProbe | Media.Core | Media.Infrastructure: 입력·출력 미디어 검사 |
| IProcessRunner | Media.Infrastructure | 같은 프로젝트: 프로세스 실행 공통화 |
| IToolLocator | Media.Infrastructure | 같은 프로젝트: 도구 탐색·검증 |

IVideoInfoService에 해당하는 Application 유스케이스는 URL 검증과 조회를 조정한다. yt-dlp JSON 파싱은 Infrastructure 책임으로 둔다. 저수준 IProcessRunner는 Application의 공개 계약으로 노출하지 않는다.

## 6. 타임코드와 정확도 정책

명세는 실제 FPS를 사용하고, 소수 FPS와 frame 변환을 고려하도록 요구하지만 29.97/59.94의 표시 체계는 확정하지 않았다. 구현 전 다음 정책을 기준으로 검증한다.

1. 초기 입력은 명세 예시와 같은 **시계 시간 + 프레임 오프셋**으로 해석한다. 시간은 `HH × 3600 + MM × 60 + SS + FF / 실제 FPS`다. 이는 정식 SMPTE non-drop-frame의 연속 프레임 번호 해석과 다르므로 두 체계를 혼용하지 않는다.
2. 분·초는 0~59, 시간은 음수가 아닌 정수다. 24시간을 넘는 영상도 허용한다. 프레임은 명세의 `FF < 실제 FPS` 조건을 따른다. 예를 들어 29.97은 0~29, 59.94는 0~59다.
3. frame 변환에서는 반올림/내림 정책을 명시한다. 소수 FPS에서 시간→프레임→시간코드가 항상 원래 문자열로 돌아온다고 가정하지 않는다. 표준 SMPTE 표시가 필요해지면 별도 정책으로 도입한다.
4. FPS는 선택한 영상 포맷을 기준으로 한다. 화질 변경으로 FPS가 달라지면 기존 입력을 다시 검증하고 사용자에게 변경을 표시한다. FPS가 없으면 임의의 30fps를 적용하지 않는다. ffprobe 등으로 확인하고, 확인 불가 시 프레임 입력·정확도 제한을 알린다.
5. 구간은 `[Start, End)`로 정의한다. `Start >= 0`, `End > Start`, `End <= Duration`를 동일한 시간 기준으로 검증한다.
6. VFR 영상에서 평균 FPS만으로 프레임 정확도를 보장할 수 없다. Phase 1에서 타임스탬프 확인과 CFR/VFR 판별 가능 범위를 검증하고, Accurate 결과의 정확도는 확인된 입력 특성 안에서 보고한다.

향후 Preview의 frame step은 실제 프레임 타임스탬프를 사용한다. 이 정책과 시간코드 표시의 연결은 Phase 5 spike에서 결정한다.

## 7. 클립 생성 흐름과 도구 책임

```text
WPF 또는 CLI
  → 메타데이터 조회 유스케이스
  → 공식 지원 URL 검증 → yt-dlp JSON → VideoInfo
  → 구간·포맷·출력 검증 → ClipJob 확정
  → 출력 및 임시 작업 공간 예약
  → yt-dlp 기반 필요한 구간 획득
  → 획득한 시간축·코덱 확인
  → FFmpeg 처리: Fast 또는 Accurate
  → ffprobe 결과 확인 → 최종 파일 확정 → 완료 결과
```

초기 기본 경로는 필요한 구간을 임시 미디어로 획득한 뒤 공통 Clip Processor에 전달하는 방식이다. 다운로드 어댑터가 yt-dlp의 구간 기능과 FFmpeg 위임을 사용할 수 있지만, 최종 처리 계획은 이미 수행한 mux/trim을 다시 불필요하게 반복하지 않는다.

원격 구간 획득은 프로토콜·키프레임에 따라 지정 범위보다 넓게 데이터를 받을 수 있다. 이를 정확한 바이트 범위 다운로드나 전체 다운로드 회피 보장으로 표현하지 않는다. 지원 소스의 실제 동작은 Phase 1에서 검증한다.

Accurate에서는 실제 획득 시작 위치와 타임스탬프를 확인한 뒤 로컬 입력 기준 IN/OUT을 계산한다. 단순히 “요청한 시작 지점이 임시 파일의 0초”라고 가정하지 않는다. 필요하면 선행 구간을 포함하여 획득한다. 시간축을 확인할 수 없는 입력은 정확도를 주장하지 않고 실패 또는 제한을 명시한다.

| 모드 | 처리 원칙 | 결과 제한 |
|---|---|---|
| Fast | MP4에 적합한 스트림을 우선 선택하고 가능한 stream copy | 키프레임 제약에 따른 시작 오차를 알림 |
| Accurate | 선택 구간을 재인코딩하여 경계 맞춤; 초기에는 전체 구간 재인코딩 허용 | 실제 프레임 타임스탬프와 입력 정확도에 따라 검증 |

MP4는 컨테이너이므로 확장자 변경만으로 호환성을 해결하지 않는다. 화질·코덱 조합을 검사하고 stream copy 가능 여부를 처리 계획에 반영한다. Fast로 처리할 수 없는 조합은 설명과 함께 Accurate 선택을 요구하며 몰래 모드를 바꾸지 않는다. H.264/AAC 출력은 호환성 기준 후보로 두고 실제 FFmpeg 빌드의 인코더 지원을 Phase 1에서 확인한다.

전체 원본 다운로드가 필요한 fallback은 기본 동작으로 넣지 않는다. 별도 사용자 선택과 용량·시간 안내가 필요한 후속 정책으로 둔다.

## 8. 상태, 취소, 출력 및 오류

### 상태와 진행률

메타데이터 상태와 작업 상태를 분리한다. 메타데이터는 Idle / Loading / Ready / Failed, 작업은 Pending / Downloading / Processing / Completed / Failed / Cancelled로 관리한다. UI 버튼 활성화와 IsBusy는 이 상태에서 파생한다.

새 조회를 시작할 때 기존 메타데이터를 무효화하고, 늦게 완료된 이전 요청은 화면에 적용하지 않는다. 실행 중인 작업은 확정된 ClipJob을 사용하므로 입력 필드 변경의 영향을 받지 않는다.

진행률은 다운로드와 처리를 구분하며, 알 수 없으면 불확정 표시를 사용한다. yt-dlp는 명시적으로 지정한 진행률 출력, FFmpeg는 구조화한 progress 출력을 어댑터에서 해석한다. stdout이 항상 JSON이라는 가정은 하지 않는다.

### 비동기와 취소

모든 장시간 작업에 CancellationToken을 전달한다. IProcessRunner는 stdout/stderr를 동시에 읽고, 비동기로 종료를 기다리며, 취소 시 자식 프로세스를 포함해 종료·대기한다. UI 스레드에서 동기 WaitForExit를 호출하지 않는다. 명령 인자는 문자열 연결 대신 개별 인자로 전달한다.

### 출력과 임시 파일

- 임시 파일은 작업 ID별 폴더로 격리한다.
- 최종 출력 대상과 같은 볼륨에 staging 파일을 두고 검사 후 최종 이름으로 이동한다.
- 기존 파일은 기본적으로 덮어쓰지 않고 충돌을 알린다. Phase 4에서는 큐 실행 전에 경로 충돌도 검증한다.
- Windows 금지 문자, 예약 이름, 끝의 공백·마침표, 빈 이름 및 경로 길이를 처리한다.
- 실패·취소 파일은 완성된 결과로 표시하지 않는다. 정리 실패는 원래 작업 오류를 덮지 않고 로그에 남긴다.

### 도구와 로그

도구 경로 탐색은 사용자 설정 → 배포 디렉터리의 tools → PATH 순서를 기준으로 한다. yt-dlp, FFmpeg, ffprobe의 필요 여부와 버전을 진단할 수 있도록 설계한다. 자동 다운로드·업데이트는 초기 범위에 포함하지 않는다.

설정·로그·작업 임시 파일은 사용자별 앱 데이터 디렉터리를 사용하고, 실행 파일 옆에 쓰기 권한이 있다고 가정하지 않는다. 진단 로그에는 종료 코드와 원시 stderr를 보존하되 인증 정보, 쿠키, 서명된 스트림 URL은 제거한다.

Application은 UnsupportedSource, InvalidRange, MissingTool, SourceUnavailable, ProcessFailed, OutputConflict 등의 오류를 구분한다. UI는 오류 코드를 사용자 문구로 변환한다. 취소는 일반 실패와 구별한다.

## 9. WPF 구성과 확장 방식

초기 화면은 MainWindow 한 개를 기준으로 한다. MainViewModel이 조회·클립 생성 유스케이스를 연결하고, 구간 입력·영상 정보·작업 표시를 책임별 ViewModel로 분리한다. 별도 화면이나 navigation framework는 필요해지는 시점까지 추가하지 않는다.

DI는 앱 Composition에서 구성한다. MVVM과 DI·logging 도구는 경량 구현을 기준으로 선택하되, 패키지·버전은 Phase 0에서 개발 환경과 함께 확정한다. ViewModel 내부의 서비스 로케이터 사용은 금지한다. 파일·폴더 대화상자는 WPF 앱 내부 계약으로 분리한다.

Phase 4 큐는 단일 ClipJob 유스케이스를 순차 호출한다. 개별 실패 결과를 보존하고 다음 작업을 계속할 수 있도록 한다. 실행 중인 목록은 snapshot으로 확정하며, 재정렬·삭제는 대기 작업에만 적용한다. “현재 작업 취소”와 “전체 큐 중단”은 별도 명령으로 정의한다.

Preview 기술은 Phase 5에서 검증하며 플레이어 타입이 Media.Core에 들어가지 않도록 한다. 브라우저 URI는 앱 경계에서 디코딩·검증한 뒤 기존 URL 조회 유스케이스로 전달한다. 로컬 영상은 ffprobe 분석 결과와 MediaInput으로 공통 Clip Processor에 연결한다.

## 10. 구현 단계와 검증 계획

| Phase | 구조 작업 | 핵심 검증 |
|---|---|---|
| 0 | 기존 앱 이동, 4개 라이브러리·테스트 경계, DI·로그·도구 경로 | 솔루션 빌드, WPF 창 실행, 테스트 실행 |
| 1 | CLI 호스트, 시간 모델, ProcessRunner, yt-dlp, 기본 FFmpeg | CHZZK/YouTube 실제 10~30초 클립, 영상·음성, 취소·누락 도구 |
| 2 | WPF 조회·구간·화질·모드·출력·진행률 연결 | UI 응답성, 입력 검증, 취소 |
| 3 | 상세 메타데이터 UI | 정보 표시, 조회 실패·요청 경합 시 상태 초기화 |
| 4 | 순차 큐, 편집·삭제·이름·선택·전체 실행·순서 변경 | 3개 이상 구간, 개별 실패 격리, 충돌·취소 |
| 5 | Preview spike 후 플레이어 연결 | 실제 frame step, IN/OUT, 핵심 모델 독립성 |
| 6 | URI scheme와 확장 | 지원 페이지에서 앱 열기·URL 전달 |
| 7 | 로컬 파일 입력 | 온라인과 동일한 구간·처리 모델 재사용 |
| 8 | UI 없는 다운로더 API와 공유 미디어 API 경계 정리 | 다운로더 호스트에서 WPF 없이 import·clip 호출; 편집기는 공유 미디어와 로컬 결과만 사용 |

단위 테스트는 시간 파싱·소수 FPS·구간 검증·URL 호스트 검증·출력 이름·JSON fixture 변환에 집중한다. Application은 가짜 다운로드·미디어 처리 구현으로 취소, 단계 전환, 실패 시 결과를 검증한다. Infrastructure는 출력량이 큰 프로세스, stderr 동시 읽기, 종료 코드, 자식 프로세스 취소와 실제 FFmpeg 결과를 검증한다.

실제 사이트 테스트는 네트워크·콘텐츠 상태 영향을 받으므로 일반 단위 테스트와 분리한다. 입력 URL·도구 버전·선택 포맷·실제 길이·시작 프레임·A/V 동기화 결과를 기록한다. 토큰과 인증 정보는 fixture에 저장하지 않는다. 단순 파일 생성만으로 Accurate 모드 완료를 선언하지 않는다.

## 11. 구현 전에 검증할 남은 결정

- 소수 FPS 시간코드: 이 설계의 시계 시간 + 프레임 오프셋 정책을 구현 기준으로 삼되, SMPTE 표시가 필요한지 별도 확인한다.
- CHZZK/YouTube: 인증 필요 콘텐츠, 누락 메타데이터, 만료 URL, 원격 seek 동작은 실제 소스로 확인한다. 인증 UX는 명세 밖이므로 요구가 확인되기 전 추가하지 않는다.
- 획득 시간축: yt-dlp 구간 추출 결과의 타임스탬프를 얼마나 신뢰할 수 있는지 Phase 1 spike로 확인한다. Accurate 처리 계획은 이 결과를 바탕으로 확정한다.
- Fast와 MP4: 제공 포맷에서 복사 가능한 코덱 선택 및 사용할 FFmpeg 인코더를 확인한다.
- tools 배포: 실행 파일 동봉 여부와 업데이트 정책은 릴리스 준비 시 확정한다.
- Preview: 원격 재생·로컬 캐시·frame step을 검증한 후 기술을 선택한다.

## 12. 명칭 및 저장소 전략 재설계 기록

- 최신 `README_FOR_REPOSITORY.md`를 재확인했다. 단일 저장소·독립 앱·필요한 공유 라이브러리라는 전략은 유지되며, ClipMaker / ClipDownloader / ClipEditor / ClipMaker.Media 명칭을 기준으로 설계를 정리했다.
- 이번 작업 대상을 ClipDownloader로 명시하고, 편집기 개발은 향후 별도 범위로 구분했다.
- 요구사항 Markdown의 이전 앱 명칭을 ClipDownloader로 변경했다. 실행 파일과 브라우저 연동 URI 예시도 동일한 명칭으로 맞췄다.
- 기존 설계에 있던 편집기의 Downloader.Application 재사용 가능 문구를 제거하고 앱 전용 계층 간 직접 참조를 금지했다.
- 미디어 Core / Infrastructure 분리와 다운로드 Application / Infrastructure 분리는 현재 다운로더 기능 및 UI 독립성을 위한 경계로 유지했다.

이번 작업의 결과는 요구사항 명칭 정리와 구조 설계 문서다. Phase 0 구현, 프로젝트 이동, 의존성 설치, 빌드·테스트·외부 영상 다운로드는 수행하지 않았다.

## 13. Phase 0 상세 설계와의 연결

앱별 문서 이동에 맞춰 요구사항 위치와 문서 트리를 갱신했다. Phase 0의 합의한 상세 결정과 검토 상태는 [Phase 0 상세 설계](phases/PHASE_0_DESIGN.md)에 기록한다. 이 문서의 테스트 프로젝트 4개는 장기 배치 계획이다. Phase 0에는 실제 도구 탐색 로직을 검증하는 Media.Infrastructure.Tests 하나를 생성하고, 나머지는 관련 로직을 구현할 때 추가한다. 미래 기능용 하위 폴더·빈 클래스를 미리 생성하지 않는 원칙은 유지한다.
