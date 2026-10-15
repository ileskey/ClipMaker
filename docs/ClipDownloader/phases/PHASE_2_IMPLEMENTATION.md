# Phase 2 구현·검증 기록

2026-10-15 (Asia/Seoul). **WPF MVP 구현·필수 검증 및 논리 단위 커밋 완료**. 검증된 제품 소스는 `9a01d56a5fbddd74f21d10accf2ace36aed2138f`다. Git Completion/완료 태그 지정은 별도로 남겨둔다. 아래 단계별 실패·수정·통과 기록과 최종 결과를 구분한다.

## 시작 상태

- Branch `phase/2`, HEAD `c0b37774e68c92464b3691c0ea12502a8cf5ba99`, 작업 트리 clean 확인.
- Phase 2 Base `3cbb04ed09df07d75dc5b963905b497909d8daeb`. 기존 1195개는 시작점 회귀이며 새 UI 검증과 구분한다.
- 상세 설계·인계·Baseline·운영 규칙·요구사항 및 실제 WPF/Application/CLI 계약을 대조했다.

## 단위 1 — 상태·비동기 수명

- metadata/작업/앱 수명 enum, 활성 Task/CTS, generation, 동일 Shutdown task, worker 도구 탐색을 추가했다.
- Closing을 보류하고 활성 작업 반환 후 실제 Close → DI → 종료 로그 → logger → SelfLog 순서를 유지한다.
- 새 Windows 호스트 테스트 프로젝트를 추가했다. 취소 후에도 늦게 반환하는 fake를 사용하여 반복 종료가 동일 task를 기다리고 늦은 도구 결과를 반영하지 않는지 검증했다.
- 첫 실행: 1개 통과. xUnit cancellation analyzer 경고 1개를 발견해 테스트 대기에 runner token을 전달하도록 수정했다. 재검증 결과는 후속 기록한다.
- 종료 경고 확인 UI는 Execute 결과 연결 단위에서 추가한다. 현재 단위만으로 실제 WPF/미디어 완료를 선언하지 않는다.

설계 보완: MainViewModel은 책임별 partial 파일로 나눈다. 서비스 계층이나 Application 계약은 추가하지 않는다.

재검증: `dotnet test tests/ClipDownloader.Tests/ClipDownloader.Tests.csproj --no-restore --nologo`에서 경고 없이 1개 통과.

## 단위 2 — metadata·입력 검증

- 네 필드 시간 입력, 기존 값 객체·FormatSelector를 통한 FPS/구간/화질 검사, FPS 미확인 시 검사 보류, 최소 metadata와 재조회 무효화를 연결했다.
- 출력은 절대 경로·기존 폴더·MP4/충돌 검사를 사용하고 worker 300ms debounce/generation으로 처리한다. dialog 취소는 기존 경로를 유지한다.
- 이름 정리는 순수 helper로 분리하고 예약 이름/금지 문자/surrogate 절단을 처리했다. 완성된 사용자 경로를 수정하지 않는다.
- `--filter FullyQualifiedName~InputTests` 첫 유효 실행 12개 통과. 테스트 프로젝트의 누락된 System.IO using으로 최초 컴파일 실패 후 수정했다. 제품 WPF 빌드 경고/오류 0.
- 추가 metadata 취소/중복·quality 재검증 사례는 다음 검증에서 함께 실행한다.
- 실제 인터페이스는 `IVideoSourceParser.Parse(Uri)`다. 설계의 문자열 overload 설명과 달리 Uri 생성 후 이 계약을 사용한다. Application 인터페이스를 변경하지 않는다.
- validation은 소규모 `INotifyDataErrorInfo` 기반 클래스로 구현했다. Toolkit ObservableValidator의 공개 보호 API에 임의 오류를 넣는 API가 없어, 필드별 서비스 오류를 직접 제공한다. 새 패키지는 없다.

Prepare/Execute 계약 연결 코드는 입력 검증의 컴파일 의존성과 함께 작성했으며 다음 단위에서 별도로 경합·실행 결과를 검증한다. 이 기록은 UI/실제 미디어 통과를 뜻하지 않는다.

추가 입력 재검증: 14개 통과 (취소 후 늦은 metadata/중복 진입·화질 변경 FPS 재검증 포함).

## 단위 3 — Prepare·확인

- 준비 요약은 반환된 PreparedClip에서 표시한다. URL/Start/End/quality/mode/output 변경 및 돌아가기·종료는 준비를 폐기한다.
- 확인 시 revision 일치와 미소비 상태를 확인하고 같은 객체를 한 번 전달한다. 다음 실행은 새 Prepare가 필요하다.
- `--filter FullyQualifiedName~PreparationTests`: 8개 통과. 모든 입력 무효화, 동일 객체 1회 실행, 새 JobId, 지연 Prepare 취소/중복 차단을 확인했다.
- 네 필드·metadata·quality/mode·출력·내부 확인·결과·도구 Expander 화면과 오류/확인 focus 연결을 추가했다. 확인 버튼은 default가 아니다.

## 단위 4 — Execute·결과·종료

- 실제 WPF DI에 CLI와 같은 서비스와 64 MiB/60초 예산을 등록했다. source owner는 한 번 등록하며 세 역할에 같은 객체를 전달한다.
- UI에서 Progress를 생성하고 worker에서 Execute를 호출한다. JobId/generation/활성 상태로 늦은 알림을 차단하고 최종 반환 상태만 성공으로 처리한다.
- 실제 범위는 정확한 유리수 초로 표시한다. 음성 없음·원본 끝 미확인·sample counts·경고·잔존 경로를 복사 가능하게 유지한다.
- ProcessTerminationFailed/예상 밖 Execute 예외는 세션 실행 잠금. 잔존 경로/종료 미확인 상태에서는 작업 종료 후 명시적 경고 확인을 기다린다.
- `--filter FullyQualifiedName~ExecutionTests`: 최초 6개 통과/1개 실패. SignedMediaTime의 기본 record 문자열을 유리수 표기로 사용할 수 없음을 발견해 명시 numerator/denominator 표시로 수정했다. 재실행 7개 모두 통과.
- 실제 Dispatcher/STA에서 progress Completed 후 실패·늦은 progress·취소 후 정리 대기·반복 종료·확정 이후 성공·비정상 percent·DI/XAML 생성을 검증했다.
- 솔루션 build 경고/오류 0. 실제 창·도구·양 사이트 media 검증은 다음 단위다.

## 단위 5 — 실제 WPF·미디어 통합 검증 (진행 중)

도구 재확인: yt-dlp 2026.08.19, FFmpeg/FFprobe 8.1, Node 22.14.0. 도구 설치·예산·quality/mode 자동 변경 없음. UI 조작은 computer-use의 sky API로 수행했다.

| WPF 생성 | 입력/선택/요청 | JobId | 결과 |
|---|---|---|---|
| YouTube Fast | qnjEE-wkw8k / Best 137+140,30fps / 10~40초 | `4aaf654f-0e7f-4761-8df8-ceceaa960083` | Succeeded, 1,584,743 bytes, 전체 A/V decode exit 0, 사용자 소리·움직임·동기 통과 |
| YouTube Accurate | 같은 영상/선택/요청 | `45af7e61-e5d5-41dc-b0f5-06cfe2bb3165` | Succeeded, 2,636,718 bytes, 전체 A/V decode exit 0, 사용자 소리·움직임·동기 통과 |
| CHZZK Accurate | 15537558 / Best hls-8384,60fps / 45~55초 | `f09e8ea2-5c82-445f-90eb-4909a8ee703f` | Succeeded, 2,883,050 bytes, 전체 A/V decode exit 0, 사용자 소리·움직임·동기 통과 |
| CHZZK Fast | 같은 영상/선택/요청 | `219d710f-0797-4f6c-bb45-19a9d5bec265` | Succeeded, 11,282,809 bytes, 전체 A/V decode exit 0, 사용자 소리·움직임·동기 통과 |

파일은 ignored `.artifacts/phase2-integration/{youtube,chzzk}-{fast,accurate}.mp4`에 보존했다. 이전 Phase의 파일·성공 증거를 재사용하지 않고 새 WPF의 Load → Prepare → 명시 확인 → Execute로 생성했다. 사용자 YouTube 확인은 이 context의 ‘둘 다 통과야’, CHZZK는 별도 질문의 ‘두 클립 모두 정상’ 응답으로 각각 기록했다. 파일 hash·크기·로컬 FFprobe 결과는 [안전한 검증 증거](evidence/PHASE_2_WPF_MEDIA.json)를 따른다.

실제 결과의 유리수 초:

- YouTube Fast: video `[68/15,1201/30)`, audio `[68/15,441088/11025)`, priming 240.
- YouTube Accurate: video/audio `[10/1,40/1)`, priming 1024 / tail 8.
- CHZZK Accurate: video `[432013/9600,528013/9600)`, audio `[45/1,55/1)`, zero 1 / overlap 2 / priming 1024 / tail 256.
- CHZZK Fast: video `[422413/9600,528013/9600)`, audio `[422413/9600,20632/375)`, priming 576.
- 모두 SourceEndVerified=false로 표시했다. Fast 실제 범위 차이와 원본 전체 끝 미확인을 성공으로 덮지 않았다.
- volumedetect: YouTube Fast mean −15.4 / max 0.0 dB, CHZZK Accurate −34.3 / −11.4 dB, CHZZK Fast −34.7 / −11.3 dB. 이는 무음이 아님을 보조하는 수치이며 청취 동기 평가를 대체하지 않는다.

실제 파일 선택 모달의 owner·MP4 filter·기존 경로 초기값·선택 반환 후 비동기 검사를 확인했다. Window resize/scroll 및 progress/결과 재그리기를 관찰했다. 입력 잠금과 준비 요약/최종 결과는 별도로 표시했다.

### 검증 중 발견·수정한 종료 결함

최초 정상 닫기 시 창·프로세스는 사라졌지만 종료 로그가 없었다. .NET Runtime 이벤트에서 `InvalidOperationException: 창을 닫는 중에는 ... Close ... 호출할 수 없습니다`와 App Closing line 47을 확인했다. 활성 task가 이미 완료된 경우 await가 동기 완료되어 원래 Closing 이벤트 안에서 재귀 Close한 결함이다. 이 실행을 정상 종료로 기록하지 않는다.

닫기 glue를 MainWindow에 두고 Dispatcher 재닫기로 수정했다. 실제 STA/Dispatcher Window.Show/Close 테스트를 추가하여 즉시 종료와 지연 취소 중 반복 닫기를 검증했다. Application은 ShutdownStarted에서 로그 경고 timer만 중단하고 OnExit의 DI → 종료 로그 → logger → SelfLog 순서를 유지한다.

수정 후 실제 idle 창 종료에서 `2026-10-15 12:48:48.795 +09:00 Application exiting with code 0`과 앱/yt-dlp/FFmpeg/FFprobe 프로세스 부재를 확인했다. 최초 실패를 통과 기록으로 대체하지 않는다.

추가 보완: 활성 Task 자체가 완료되기 전 새 진입을 막아 상태 Ready 알림의 재진입을 차단했다. 출력 재검사 task들도 종료 대기에 포함하고, 경고 확인은 경로 검사 종료 후만 허용한다. 잔존 자원 경고는 다음 결과에도 보존하며 이력/queue는 추가하지 않았다. 성공 후 출력 충돌 상태를 다시 검사하고 오류·확인 focus에 BringIntoView를 적용했다. 이름 정리의 제어문자/예약 stem 처리도 보완했다.

이 시점 재검증: 새 호스트 테스트 35개 통과·실패/skip 0. 솔루션 build 경고/오류 0. 기존 회귀는 Core 143 / Media Infrastructure 193 / Downloader Infrastructure 859, 총 1195개 통과·실패/skip 0. 당시 남았던 실제 취소·반복 닫기/자식 종료·잔존 자원 및 CHZZK 사용자 확인은 아래 최종 결과로 완료했다.

### 최종 실제 취소·종료·회귀 결과

- 수정 빌드의 `65da94b4-0710-45de-9369-0bd3035466a9`: 실제 FFprobe 자식 PID 33144를 관찰한 작업에서 취소 버튼 사용. 최종 `Cancelled` 표시, 자식 도구/해당 작업 폴더/확정 출력 없음. 취소 직후 새 작업이 가능한 시점은 정리와 경로 재검사 종료 후다.
- 수정 빌드의 `f5364bc2-58b7-4494-a4fe-b32a83674fca`: 실제 처리 중 Alt+F4. 최종 로그는 12:54:26.635 Cancelling → .636 Cleaning → .647 returned Cancelled → .721 Application exiting with code 0. 닫기 직전 FFprobe 자식 PID 35584도 관찰했다. 앱/yt-dlp/FFmpeg/FFprobe 및 해당 작업 폴더 없음. `cancel-download.mp4`, `cancel-close.mp4`, `.clipmaker-*.tmp.mp4` 없음. 사용자 정상 출력 네 파일은 보존했다.
- 실제 작업 중 창 이동 입력·progress 애니메이션·단계 재그리기를 확인했다. 동기 서비스 preamble이 worker에서 실행되는지도 Dispatcher 테스트로 검증했다.
- 반복 닫기는 실제 WPF Window.Show/Close를 사용한 제어 가능한 지연 서비스 테스트에서 두 번 Close해 동일 취소/정리 대기를 생략하지 않음을 확인했다. live 작업은 정리가 빠르게 완료되어 두 번째 사용자 닫기 전에 정상 종료했다. 이를 live 반복 닫기 두 번을 관찰한 것으로 확대하지 않는다.
- 정상 idle 종료는 앞 절의 수정 후 코드 0 증거를 따른다. 종료 수명 glue는 MainWindow의 task 대기/Close latch와 App의 OnExit DI/logger 순서로 연결된다. 예상치 못한 실행 오류·종료 미확인·잔존 경고 확인 경로는 fake 결과/Dispatcher 테스트로 검증했으며 실제 프로세스 종료 실패를 강제로 유발하지 않았다.
- 생성 시 빌드 이후 발견한 수명·재진입·경고 표시 보완은 미디어 처리 정책을 변경하지 않았다. 보완 후 실제 취소/정상 종료와 호스트 전체 테스트를 재검증했다. 마지막 문구 보완은 선택 경고의 한국어 표시와 같은 URL 재조회 시 이전 선택 즉시 제거다.
- 최종 `dotnet build ClipMaker.slnx --no-restore --nologo`: 성공, 경고/오류 0.
- 최종 `dotnet test tests/ClipDownloader.Tests/ClipDownloader.Tests.csproj --no-build --no-restore --nologo`: 35개 통과, 실패/skip 0 (Input 15 / Preparation 8 / Execution 9 / Lifetime 1 / WindowClosing 2).
- 기존 각 test csproj의 `dotnet test --no-build --no-restore --nologo`: Core 143 / Media Infrastructure 193 / Downloader Infrastructure 859, 합계 1195개 통과, 실패/skip 0. 이번 작업에서 관찰한 총 통과 수는 **1230개**다.
- `git diff --check` 통과. 시작 branch/HEAD 유지, 새 제품 변경은 WPF 호스트/호스트 테스트/솔루션 및 Phase 2 기록에 한정한다. Application·Infrastructure·Core·CLI의 정책과 제품 소스는 수정하지 않았다.

## 최종 범위와 Git 상태

필수 기능·실제 양 사이트/양 모드 생성·재생/동기·UI 응답·취소·종료 수명·소유 자원 검증을 확보했다. 일부 CHZZK metadata 실패, 먼 위치 Accurate·원본 획득/playlist/segment/codec/clock 지원 경계는 그대로다. Queue·Player·ClipEditor·도구 자동 설치·설정 영속화는 추가하지 않았다.

최초 구현 보고는 `phase/2`, HEAD `c0b37774e68c92464b3691c0ea12502a8cf5ba99` 위 미커밋 상태였다. 단위별 커밋 자동 진행 방침을 누락했으며, 사용자 후속 지시로 아래처럼 현재 변경을 정리했다. 당시 각 구현·검증 직후 커밋이 존재했던 것처럼 소급 기록하지 않는다.

### 사용자 후속 지시에 따른 커밋 정리

| 단위 | 커밋/범위 | 커밋 직전 실제 검증 |
|---|---|---|
| 입력·파일 선택 기반 | `9eeafc9` — 필드 오류·TimecodeInputViewModel·파일명 정리·dialog 계약/구현, 5개 파일 | staged-only 스냅샷 솔루션 build 경고/오류 0, 해당 스냅샷을 참조하는 직접 helper 검사 9개 통과 |
| WPF 통합·종료·테스트 | `9a01d56a5fbddd74f21d10accf2ace36aed2138f` — metadata/Prepare/Execute·DI·화면·종료·호스트 테스트, 17개 파일 | staged-only 스냅샷 build 경고/오류 0, Core 143 / Media Infrastructure 193 / Downloader Infrastructure 859 / WPF 35, 총 1230개 통과·실패/skip 0 |
| 검증·설계·Baseline 기록 | 이 절과 실제 미디어 증거·설계 상태를 포함하는 문서 커밋 | JSON 구조·미디어 네 파일 SHA256/크기·신규 문서 링크·staged whitespace 확인. 제품 소스 변경 없음 |

단위 1과 2는 `git checkout-index --all --prefix=...`로 인덱스의 파일만 ignored `.artifacts/phase2-commit-validation/01-input-foundation`, `02-wpf-workflow`에 추출했다. 두 번째 스냅샷 내부를 작업 디렉터리로 하여 각 테스트 프로젝트를 실행했다. 미스테이징 제품 코드나 기존 bin/obj에 의존해 통과한 결과가 아니다. 첫 단위의 helper 검사는 `.artifacts/phase2-commit-validation/input-helper-checks`에 보존하며 영구 xUnit 1230개와 별도 검사다.

첫 격리 빌드의 기본 병렬 restore/build는 exit 1·표시 오류 0으로 끝나 원인을 확정하지 않았다. 별도 프로젝트 restore에서 네트워크 제한으로 NuGet 취약성 데이터 조회 경고 NU1900도 관찰했다. 이후 기존 패키지 캐시로 `dotnet restore ... --ignore-failed-sources -p:NuGetAudit=false -m:1`, `dotnet build ... --no-restore --disable-build-servers -m:1`을 실행해 두 스냅샷 모두 경고/오류 0을 확인했다. 이는 격리 검증 명령의 일회성 옵션이며 저장소 패키지/보안 설정은 변경하지 않았다. 취약성 데이터 조회를 수행한 것으로 주장하지 않는다.

현재 검증된 제품 소스 기준은 `9a01d56a5fbddd74f21d10accf2ace36aed2138f`이고 후속 문서 커밋은 이 제품 소스를 변경하지 않는다. 구현·검증 및 커밋과 공식 Phase Completion/태그 지정을 구별한다. Baseline에는 검증된 제품 기준을 명시하되 Completion/태그는 임의 지정하지 않으며, 원격 push도 수행하지 않는다.
