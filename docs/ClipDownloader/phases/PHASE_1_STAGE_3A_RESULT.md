# Phase 1 / 3단계 첫 커밋 단위 — 공식 URL 검증·정규화

수행일: 2026-10-10 (Asia/Seoul).

상태: **3a 구현·자동 검증 완료, 커밋 대기**. 3b 메타데이터 조회/변환과 3c 포맷 선택은 아직 구현하지 않았다. URL 문법 통과는 실제 영상 존재·접근·완료 상태·FPS 확인의 성공 기록이 아니다.

## 시작·변경 범위

- branch `phase/1`, 시작 HEAD `aafaac43833704525c322b1e67cecaee2cbb2828`: `feat: Add tool execution and capability diagnostics`. 시작 작업 트리는 깨끗했다.
- Application/Models/VideoSourceReference.cs: 플랫폼 enum, ID 및 canonical PageUrl을 갖는 불변 소스 참조. factory가 기본 provider ID와 정규화 URL의 일관성을 보장한다.
- Infrastructure/Sources/SupportedVideoSourceParser.cs: 문자열/Uri 입력의 문법·공식 source 판별·정규화. 순수 parser이며 프로세스·파일·네트워크를 호출하지 않는다.
- 기존 Downloader.Infrastructure.Tests에 SupportedVideoSourceParserTests.cs·VideoSourceReferenceTests.cs를 추가했다. 프로젝트·패키지·솔루션·WPF·기존 도구/프로세스/시간 코드는 바꾸지 않았다.
- staging/commit/branch/tag 변경을 수행하지 않았으며 기존 사용자 변경은 보존했다.

## 채택 정책의 구현

- 명시적인 scheme://를 요구하고 HTTPS·기본 포트 443만 허용한다. 명시 :443과 숫자상 동일한 leading-zero 포트는 허용하며 빈 포트는 형식 오류다.
- 앞뒤 공백은 제거한다. 내부 literal whitespace/control/backslash, 잘못된 percent escape는 거부한다. 사용자명/비밀번호뿐 아니라 빈 사용자명 @도 거부한다. 오류는 원본 URL·인증값을 echo하지 않는다.
- 호스트는 정확한 allowlist로 DNS 대소문자를 구분하지 않고 비교한다. YouTube watch는 youtube.com/www.youtube.com/m.youtube.com, short link는 youtu.be, CHZZK는 chzzk.naver.com만 허용한다. 유사 suffix/subdomain, www.youtu.be, trailing dot, Unicode homograph, IP literal은 허용하지 않는다.
- CHZZK는 literal /video/{ID}, YouTube는 literal /watch?v={ID} 또는 youtu.be/{ID}만 허용한다. Shorts/embed/live/playlist 전용·trailing/추가 경로는 현재 범위 밖이다.
- watch URL의 list·추적 값·fragment·URL 시작 시간은 제거하며 최종 정규화 query는 v 하나다. 경로 기반 CHZZK/short link의 query는 ID 선택에 사용하지 않고 제거한다. 요청 구간은 이후 --start/--end만 담당한다.
- ID의 대소문자는 보존한다. 정규화 URL은 https://www.youtube.com/watch?v={ID} 또는 https://chzzk.naver.com/video/{ID}이며 입력 alias로 동일한 value reference를 만든다.

## 구현 전 남아 있던 하위 URL 경계의 구체화

| 항목 | 현재 구현 기준 |
|---|---|
| YouTube ID | ASCII A~Z/a~z/0~9/_/- 정확히 11자리 |
| CHZZK ID | 비어 있지 않은 ASCII 숫자 문자열. int/long으로 해석하지 않으며 leading zero를 보존 |
| ID escape | path ID/query key·value에서 한 번만 percent decode. escaped unreserved ID 문자는 허용, decode 후 slash/space/NUL/비정상 UTF-8·잔여 percent 등은 ID 문법에서 거부 |
| 고정 경로 escape | /watch, /video/ 같은 고정 경로는 원문 literal로 검사. escaped 경로·dot traversal이 Uri 정규화로 공식 경로가 됐다고 허용하지 않음 |
| watch query | & 구분, key v는 대소문자 구분. %76은 decode 후 v로 취급. 누락/빈/중복 v는 InvalidInput, 동일 ID 중복도 거부 |
| + | form-urlencoded의 공백 변환을 하지 않음. ID에 +는 허용 문자가 아니므로 거부 |
| Uri 입력 | OriginalString을 같은 parser에 전달. CLI는 string 입력 경로로 원문을 검사해야 하며 이전 호출자가 이미 rewrite한 문자열을 복구한다고 주장하지 않음 |
| 오류 | URL 형태/ID/모호성은 InvalidInput, 미지원 scheme/host/port/path는 UnsupportedSource. 모두 Input·Field=Url이며 결과 Data 없음 |

YouTube의 기본 ID 문법은 [yt-dlp 공식 extractor](https://github.com/yt-dlp/yt-dlp/blob/master/yt_dlp/extractor/youtube/_video.py)의 11자리 ASCII 패턴을 대조했다. extractor가 허용하는 다른 URL 유형·첫 v 선택 정책까지 수입하지 않았다. CHZZK 숫자 문법은 채택한 공식 VOD 경로 요구를 따른다. video 존재/ID 접근성은 이후 조회로 확인한다.

[Uri.OriginalString](https://learn.microsoft.com/en-us/dotnet/api/system.uri.originalstring?view=net-10.0), [UnescapeDataString](https://learn.microsoft.com/en-us/dotnet/api/system.uri.unescapedatastring?view=net-10.0)와 현재 runtime 동작을 대조했다. 고정 경로까지 RFC normalization으로 넓게 받는 선택 대신, 현재 공식 경로 범위를 원문 구조로 지키는 방안을 택했다. ID 자체의 안전한 escape는 기본 문법에 맞게 허용한다. 일반 CLI/지원 정책을 새로 승인받거나 지원 사이트를 확장한 결정은 아니다.

## 실제 Uri 관찰·실패 보완

- 현재 runtime에서 /foo/../watch와 /%77atch의 AbsolutePath는 /watch로 정규화됐다. UserInfo는 빈 @에서도 빈 문자열이었고 : 빈 포트는 443으로 해석됐다. 따라서 원문 path/authority도 검사한다.
- Windows Uri는 protocol-relative //host/...를 file URI로 해석할 수 있었다. 명시 scheme:// 검사로 이를 URL 형식 오류로 처리한다.
- 최초 테스트는 3a 106개 중 2개 실패였다. 100,000자리 URI를 받아들이지 못할 것이라는 테스트 가정이 현재 .NET 10에서 틀렸고, FTP 입력은 scheme 검사 전 well-formed 검사에서 InvalidInput으로 분류됐다. 불필요한 길이/정수 제한을 추가하지 않고 테스트를 실제 동작에 맞췄으며 비-HTTPS scheme을 먼저 미지원으로 분류했다.
- 후속 110개 중 protocol-relative 분류 1개가 실패해 명시 scheme 검사를 보완했다. 모든 실패는 최종 성공 증거로 사용하지 않았고 이후 전체 검증에서 통과했다.
- 100,000자리 숫자 ID의 model 생성은 현재 runtime에서 통과했다. 이는 확인한 한 입력의 URI 표현 가능 결과이며 무한 길이/모든 환경 지원을 보장하지 않는다. factory는 canonical URI를 표현할 수 없으면 false를 반환한다. 실제 사이트에서 유효한 ID라는 뜻도 아니다.

## 최종 검증

| 명령·대상 | 실제 결과 |
|---|---|
| dotnet build ClipMaker.slnx --no-restore --verbosity minimal (승인된 실행) | exit 0, 경고 0·오류 0 |
| dotnet test ClipMaker.slnx --no-build --verbosity minimal (승인된 실행) | exit 0; Core 85·Media.Infrastructure 73·Downloader.Infrastructure 156, 총 314 통과; 실패/건너뜀 0 |
| 신규 3a | URL parser 98·source model 12 = 110; 네트워크/설치 도구와 독립 |
| 기존 회귀 | 2c 46·시간 85·프로세스/도구 탐색 73 통과 |
| git diff --check·신규 파일 whitespace 검사 | 오류 없음 |

검증은 공식 alias/host casing, port 443, tracker/list/time/fragment 제거, 한 번 escape decode·ID 대소문자, 중복 v, 인증값 비노출, host/ID Unicode·IP literal, Uri normalization 경계, null/relative Uri, 큰 ID·모델 canonical 일관성을 포함한다.

실제 사이트·메타데이터 조회·JS/EJS 확인·영상 다운로드·미디어 생성·WPF 실행은 수행하지 않았다. 제공된 CHZZK/YouTube URL이 문법상 허용되는 것과 실제 완료 영상인지는 구분한다.

## 커밋 지점·다음 단위

권고 메시지: `feat: Add supported video URL validation and normalization`.

후보는 신규 모델/파서 2개·테스트 2개, 본 결과·상세 설계·Baseline 상태 반영이다. 실제 staging/commit은 아직 없으며 여기서 멈춘다.

다음은 **3b yt-dlp JSON 메타데이터 변환·조회**다. 실제 조회 전 YouTube 의존성·사용 도구 옵션/버전과 민감값 제거 fixture 준비를 연결해야 한다. FPS/시간축/Best/코덱 전략의 실험 의존 부분과 Phase 1 Completion/tag는 미확정으로 유지한다. Phase 2·WPF 다운로드 UI·ClipEditor는 진행하지 않는다.
