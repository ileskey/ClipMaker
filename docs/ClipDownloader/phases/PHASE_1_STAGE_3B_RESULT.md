# Phase 1 / 3단계 두 번째 커밋 단위 — 메타데이터 조회·JSON 변환

수행일: 2026-10-10 (Asia/Seoul).

상태: **3b 구현·자동 검증·두 사이트 metadata-only 조회 완료, 커밋 대기**. 3c 포맷 선택·실제 FPS/CFR 측정·시간축·획득/처리·전체 Phase 1 검증은 미완료다.

## 시작·변경 범위

- branch `phase/1`, 시작 HEAD `86359133cd5c53eb46af3584192d3da0a0c594b2`: `feat: Add supported video URL validation and normalization`. 시작 작업 트리는 깨끗했다. Phase 0 기준 `0759dc52475538df848f765ce30c005cf755107f`는 유지한다.
- Application: `IVideoInfoService`, 불변 `VideoInfo`/`VideoFormat`, 영상 상태·stream presence·값을 포함하지 않는 필드 진단.
- Infrastructure/YtDlp: 조회 서비스, JSON parser, 정확한 숫자 변환 helper. 기존 tool locator/settings·실행 진단·ProcessRunner를 사용한다.
- 기존 Downloader.Infrastructure.Tests에 parser/service 테스트와 익명화 실제 site fixtures를 추가했다. fixture 복사 설정 외 프로젝트/패키지/솔루션 구성을 확장하지 않았다.
- 제품 CLI·WPF DI·다운로드 UI·ClipEditor·Phase 2는 변경하지 않았다. 도구/패키지/runtime 설치·Git staging/commit/tag/branch 변경은 수행하지 않았다.

## 조회와 의존성

- URL 검증·canonical 소스 → info 범위 yt-dlp 실행/버전 진단 → YouTube Node 위치/버전 검사 → JSON 조회 → 변환 순서다. CHZZK에는 Node를 요구하지 않는다. 다른 도구를 info에서 진단하지 않는다.
- 현재 최소 YouTube 전략은 기존 `ToolPathSettings`의 `node.exe` 설정/locator로 찾은 Node **22 이상**을 명시적으로 사용한다. Deno/Bun/QuickJS 자동 선택·설치·fallback 프레임워크는 추가하지 않았다. 명시 경로 실패 시 다른 경로로 대체하지 않는다. 실제 검증은 설치된 Node 22.14.0으로 수행했다. fnm 세션 경로를 제품 기본값이나 저장 설정에 박아 넣지 않았다.
- [yt-dlp 2026.08.19 README](https://github.com/yt-dlp/yt-dlp/blob/2026.08.19/README.md)와 [공식 EJS 안내](https://github.com/yt-dlp/yt-dlp/wiki/EJS)의 runtime 옵션·Node 최소 버전·공식 exe의 EJS 포함 정책을 대조했다. 아래 설정으로 실제 사용자 YouTube URL 조회가 성공했다. 모든 영상·환경의 EJS 성공을 보장하는 증거는 아니다.
- 고정 옵션: `--ignore-config --no-plugin-dirs --no-cache-dir --no-remote-components --no-js-runtimes --simulate --skip-download --dump-single-json --no-playlist --ignore-no-formats-error --retries 2 --extractor-retries 2 --fragment-retries 2 --file-access-retries 0 --abort-on-unavailable-fragments`. YouTube는 `--js-runtimes node:<선택 절대경로>`, 마지막에 `-- <canonical URL>`을 전달한다.
- 내부 retry만 최대 2회 추가이며 서비스 전체를 재시도하지 않는다. metadata 실행 120초, yt-dlp/Node 사전 진단 각각 15초다. 종료 정리는 기존 runner의 독립 예산을 따른다. 전체 API가 120초 안에 끝난다는 뜻은 아니다.
- stdout Full 32MiB, stderr Tail 256KiB, callback 없음. 원시 JSON/stderr·인증·서명 media URL은 공개 결과/로그/fixture에 복사하지 않는다. config·browser cookies·remote JS components·플러그인을 읽거나 설치하는 옵션을 넣지 않았다.

## JSON 하위 계약

- 완전한 UTF-8·단일 JSON object, 최대 32MiB·depth 64, 요청 source와 같은 문자열 id를 요구한다. 중복 id/type/formats·playlist/entries·잘림·추가 JSON·불일치 ID는 InvalidMetadata다. `_type`은 누락 또는 video만 허용한다. 알 수 없는 필드는 무시한다.
- title/uploader/duration/thumbnail/upload date는 nullable다. CHZZK의 uploader 누락/null은 channel로 보완한다([고정 버전 공식 extractor](https://github.com/yt-dlp/yt-dlp/blob/2026.08.19/yt_dlp/extractor/chzzk.py)). 잘못된 타입·값·범위는 해당 값 미확인과 안전한 필드 진단으로 보존한다.
- thumbnail은 well-formed HTTPS DNS URL이며 userinfo/query/fragment가 없어야 공개한다. query가 서명인지 추정하거나 임의 제거해 다른 URL로 만들지 않고 보수적으로 null+UnsafeReference로 처리한다.
- not_live/was_live는 Completed, is_live는 Live, is_upcoming은 Upcoming이다. post_live/미확인 상태는 Unknown이며 상충하는 is_live는 Ambiguous다. 성공적인 info는 clip 실행 가능성 판정이 아니다.
- format_id 누락/잘못된 타입·전체 중복 ID 항목·명시 DRM·양쪽 codec none인 비미디어 항목을 제외한다. codec 누락은 Unknown, none은 Absent, codec 문자열은 Present다. stream 누락을 오디오/비디오 없음으로 추정하지 않는다. 빈 formats도 info 성공으로 허용한다. Best/정렬/실행 후보 선택은 3c에 남긴다.
- FPS는 format별 nullable FrameRate이며 전역 FPS가 없다. JSON 숫자만 정확한 십진 유리수로 변환한다. 29.97은 2997/100이며 30000/1001로 교정하지 않는다. 약분 후 long 범위 밖·0·음수는 unknown이다.
- 해상도는 양의 정수/int 범위, duration은 양수·TimeSpan 범위 검사 후 nearest-even tick으로 반올림한다. 양수 sub-tick은 0 tick+Rounded 진단으로 표현하며 실행 가능 길이로 보증하지 않는다. 원래 값이 MaxValue보다 크면 반올림으로 숨기지 않는다. 거대한 지수에 거대한 10의 거듭제곱을 생성하지 않는다.
- 취소와 timeout은 종료 증거가 있을 때 구분한다. 미확인 종료는 ProcessTerminationFailed, 불완전/상한 초과 stdout은 InvalidMetadata다. 일반 비정상 종료는 ProcessFailed이며 stderr 문자열로 비공개/삭제/로그인 원인을 추정하지 않는다. 명시 JSON DRM source는 SourceUnavailable다.

## 실제 실행·검증

환경: Windows 11 x64, SDK 10.0.302/runtime 10.0.10, yt-dlp 2026.08.19 공식 winget exe, Node 22.14.0. 기존 설치 절대경로를 임시 harness 설정에만 전달했다.

| 실제 조회 | exit / 종료 증거 | stdout | 공개 변환 결과 |
|---|---|---|---|
| CHZZK video/15430558 | 0 / root·자식·reader 모두 확인 | 10,218 bytes | Completed, 2,667초, format 5개, 30/60 FPS, H.264+AAC |
| YouTube watch?v=sFXPM_sfJNo | 0 / root·자식·reader 모두 확인 | 637,573 bytes | Completed, 226초, 전체 47개 중 storyboard 4개 제외 → 미디어 format 43개, 분리 A/V·codec 미확인 항목 보존 |

두 조회에서 정해진 challenge failure/runtime missing/network failure 문자열은 관찰되지 않았다. 모든 stderr 경고가 없었다는 검증은 아니다. 설치·영상/부가 파일 저장·서명 URL 재사용은 하지 않았다. 원시 응답은 메모리에서만 처리했다. fixture는 명시한 scalar/format allowlist로 투영하고 title/uploader를 익명화했다. fixture README에 보존 필드와 관찰 한계를 기록했다. 임시 package-free harness는 솔루션에 넣지 않고 검증 후 정리했다.

- 최종 `dotnet build ClipMaker.slnx --no-restore`: 경고 0·오류 0.
- `dotnet test ClipMaker.slnx --no-build --no-restore`: Core 85·Media.Infrastructure 73 통과. Downloader 테스트는 실제 YouTube format 수를 42로 잘못 집계한 기대값 한 개가 실패했다. 원본 fixture 47개와 제외 4개를 확인해 43으로 수정했다. parser가 실제로 반환한 43개는 맞았다.
- 수정 후 `dotnet test tests/ClipDownloader.Infrastructure.Tests/ClipDownloader.Infrastructure.Tests.csproj --no-restore`: **217개 전부 통과**. 최종 검증 합계 **375개**, 3b 신규 61개. 수정은 테스트 기대값/기록에 한정되어 통과한 다른 테스트를 불필요하게 반복하지 않았다.
- 최초 테스트 컴파일에서 ReadOnlyMemory<byte>에 collection expression을 직접 전달한 테스트 코드 3곳이 실패했다. byte[]/Array.Empty<byte>()로 수정했다. 실패를 완료로 숨기지 않는다.
- 숫자 경계·초대형 지수·UTF-8/크기·중복/누락·DRM/stream/state·실제 익명화 fixture·안전한 오류·취소/timeout/종료 미확인·불완전 stdout·runtime 미확인 중단·canonical metadata-only 인자를 검증했다. 자동 테스트는 네트워크를 호출하지 않는다.

## 다음 커밋 단위

3b의 논리적 커밋 지점이다. 권장 메시지: `feat: Add video metadata retrieval and JSON validation`.

3c는 포맷 선택과 관련 E2 근거·필요한 codec/FPS 정책을 확인한 뒤 진행한다. metadata 선언 FPS는 실제 CFR·프레임 정밀도·선택 format 다운로드 성공을 입증하지 않는다. 미디어/FPS 측정·구간 획득·시간축·출력 검증·CLI 및 Phase 1 전체 완료는 아직 남아 있다.
