# ClipDownloader Phase 3 구현·검증 기록

2026-10-15 (Asia/Seoul). **In Progress / Pending**. 필수 실제 검증 전 Completion/Passed를 부여하지 않는다.

## 단위 0 — Git 기준 확인

- 사용자 지정 branch `phase/3`, HEAD/Base `5d5a06e6a6d0b8ec8ea952e738ecf99756a53712`. HEAD 직접 tag 없음.
- `phase-2-complete^{}` = `b4ef12692da9268c72c2a42e6c0ab73351d7bb9f`. 검증 제품 `86fd9f8`가 Completion의 ancestor임을 확인했다.
- Completion 이후 committed diff: 문서·증거 7파일 +139/-10, `apps src tests` diff 없음. staged/unstaged tracked 변경 없음.
- 시작 untracked는 사용자 제공 `PHASE_3_DESIGN.md`, `PHASE_3_IMPLEMENTATION_HANDOFF.md` 두 파일. 내용을 보존하며 본 준비 커밋에는 포함하지 않는다. ignored `.artifacts`를 보존한다.
- 운영 규칙·Baseline·요구사항·구조·Phase 2 설계/실제 기록·미디어 JSON·Phase 1 지원 경계와 현재 Input/Job/Window/App/DI/parser 소스를 대조했다.
- 기존 미디어 64 MiB/60초·playlist/segment·codec/clock·FPS null 및 PreparedClip 실행 계약은 유지 대상이다. Phase 2의 1,230개와 실제 네 클립은 과거 증거이며 이번 재실행 결과가 아니다.

## 검증 상태

단위 4 실제 통합·최종 검증은 아직 수행하지 않았다. 새 패키지·도구 설치·원격 push·기존 태그 변경 없음.

## 단위 1 — 상세 텍스트·선택 projection

- 불변 MetadataPresentation, 같은 높이 helper의 Quality/해상도 목록, 선택 FPS rational/보조 근사, total hours/소수 길이, 안전한 원본 index 진단을 추가했다. 복사·wrap·스크롤·AutomationId/Name을 XAML에 연결했다. 이미지 획득은 아직 없다.
- Ready 통지 전에 선택/projection을 일치시킨다. stale metadata 일반 화면 갱신을 차단하고 ProcessTerminationFailed/잔존 자원은 guard 전 누적한다.
- 최초 build는 새 테스트에서 존재하지 않는 enum 이름 사용으로 오류 1개. Execution으로 수정 후 `dotnet build ClipMaker.slnx --no-restore --nologo` 경고/오류 0.
- `dotnet test tests/ClipDownloader.Tests/ClipDownloader.Tests.csproj --no-build --no-restore --nologo`: 40 통과, 실패/skip 0 (기존 35 + 신규 5). STA 실제 Window binding·Ready snapshot·stale 경고, exact FPS/null·긴 길이·해상도 필터 포함.
- 실제 양 사이트·DPI·원격 이미지 검증은 단위 4에 남아 있다. 기존 selector/Prepare/Execute/미디어 bytes는 변경하지 않았다.

## 단위 2 — 제한 HTTP 계약·구현

- Application의 독립 bytes 결과(factory 복사/최대 4 MiB)와 전용 HttpClient 소유자를 추가했다. WPF/CLI에는 아직 연결하지 않았다. 무인증·무쿠키·무proxy·무redirect·HTTP/1.1 exact, 전체 10초 deadline, JPEG/PNG MIME/signature, bounded streaming과 정리를 적용했다. 로그에 URL/body/예외를 남기는 코드가 없다.
- 주소 표: IPv4 0/8, 10/8, 100.64/10, 127/8, 169.254/16, 172.16/12, 192.0.0/24, 192.0.2/24, 192.88.99/24, 192.168/16, 198.18/15, 198.51.100/24, 203.0.113/24, 224/3 차단. mapped IPv4는 정규화. IPv6는 2000::/3만 후보로, 2001::/23, 2001:db8::/32, 2002::/16, 3fff::/20 및 scope ID를 거절한다.
- [IANA IPv4](https://www.iana.org/assignments/iana-ipv4-special-registry/), [IPv6](https://www.iana.org/assignments/iana-ipv6-special-registry/)를 2026-10-15 확인했다. 특수 프로토콜용 globally routed 예외도 보수적으로 거절한다. 이는 정상 CDN의 수용 범위를 줄일 수 있으나 특수/전환 주소 허용을 피하는 선택이다. NAT64/6to4/Teredo 우회는 지원하지 않는다.
- DNS 결과 중 하나라도 거절이면 connect 없음. 모두 승인 시 첫 승인 IP로 1회 직접 연결하며 URI host의 기본 TLS 검증을 유지한다. 연결 재사용은 꺼서 재조회마다 목적지를 검사한다. 새 범용 네트워크 계층이나 패키지는 없다.
- `dotnet build ClipMaker.slnx --no-restore --nologo`: 최종 경고/오류 0. 새 TLS 테스트의 CA2022 경고는 반환 읽기 수를 명시적으로 버리도록 수정했다.
- Infrastructure `dotnet test ... --no-build --no-restore --nologo`: 940 통과, 실패/skip 0 (기존 859 + 신규 81). URI·주소 경계·mixed DNS·고정 IP·헤더/본문 timeout·caller 취소·MIME/encoding/길이·4 MiB/+1·정리 및 기존 CLI/parser 회귀 포함.
- 실제 socket/TLS 제어 endpoint: 테스트 내부 connector만 승인 fixture IP를 loopback listener로 연결, 실제 SslStream에서 SNI images.example.com 관찰 및 자체 서명 인증서 거절 확인. production gate/인증서 callback은 완화하지 않았다. 실제 신뢰 인증서 성공과 양 사이트 이미지는 단위 4에서 확인한다.

## 단위 3 — WPF decode·이미지 수명·DI

- bounded PNG/JPEG 헤더 preflight(양축 4096, 8 MP), PNG chunk 길이/CRC/animation 거절, JPEG SOF와 끝 검사, WIC 원본 치수/단일 frame 대조, 최대 640×360 aspect decode, OnLoad/Freeze/stream 해제를 추가했다.
- metadata revision/독립 epoch/slot/token/lifetime guard와 CTS owner를 연결했다. 이전 작업 실제 반환 뒤 새 worker 획득/decode를 직렬 실행한다. 이미지 Loading/실패/취소는 Prepare·입력·확인 identity에 영향을 주지 않는다. 별도 취소·상태/이미지 접근성 binding을 추가했다.
- shutdown은 이미지 참조 제거/취소 후 foreground/path/image 완료를 기다리며 경고 확인도 이미지 완료까지 금지한다. 수명 알림 전에 shutdown task를 등록해 재진입에도 같은 task를 돌려준다. App의 Dispatcher 지연 Close → DI → 종료 로그 → logger/SelfLog 순서는 변경하지 않았다.
- 최초 신규 테스트: 59개 중 3실패. WIC가 CRC 손상 PNG를 수용해 preflight CRC 검사를 추가했다. 재조회 시간 Reset이 inputRevision을 바꾸므로 초기 요청 revision guard를 이미지 시작에 그대로 쓰면 새 이미지도 막혔다. 성공 snapshot identity/metadata revision 기반으로 보완해 재실행 59통과, 추가 경고/재진입/실패 재조회 검사 후 최종 61통과·실패/skip 0.
- 최종 `dotnet build ClipMaker.slnx --no-restore --nologo` 경고/오류 0 및 WPF `dotnet test ... --no-build --no-restore --nologo` 61개 통과. 작은 JPEG/PNG·큰 정상/4096/8 MP·초과/손상/animation·frozen 픽셀 접근, Dispatcher worker, 이미지 대기 중 Prepare/확인/동일 객체 실행, stale URL/동일 URL·실패 재조회·취소 무시 decode 직렬화, 실제 Window.Show/Close 두 번과 이미지 종료 전 닫기 금지, 경고 확인·reentrant Shutdown 포함.
- in-process WIC 강제 중단/전체 메모리 상한은 보장하지 않는다. 동기 decode가 돌아올 때까지 종료도 기다린다. UI 실작업 테스트와 실제 원격 관찰은 구분한다.
- 7.3 영향 분석: selector/시간 변환/Prepare/Execute/media-source DI/원본 예산·미디어 정리 코드는 그대로다. 새 DI는 이미지 전용이며 기존 정리 완료 대기에 이미지 owner만 추가했다. 기존 미디어 정리 계약을 교체하지 않았다. 따라서 네 클립 재생성은 요구하지 않고 Phase 2 증거를 유지하며, 단위 4에서 실제 WPF 준비 연결·종료와 전체 회귀를 수행한다.

## 단위 4 보완 — 알림 재진입 guard

- 실제 양 사이트 검증 후 소스 재검토에서 PropertyChanged 수신자가 URL을 바꾸면 이미지/metadata의 여러 순차 알림 사이에 snapshot이 바뀔 수 있음을 확인했다. 채택 전 guard만으로는 이어지는 Ready/상태 알림을 보호하지 못하므로 각 채택 알림 뒤 snapshot identity를 다시 검사한다. 이미지 작업 등록/직렬 대기는 그대로 유지한다.
- 조회 초기 알림 중 URL 변경도 원래 요청과 대조한다. 성공 시 시간 Reset에 따른 정상 inputRevision 변경과 metadata identity 변경을 구별한다. 선택·Prepare·미디어 계약 변경은 없다.
- 새 재진입 사례 5개 포함 WPF 66개 통과·실패/skip 0 (`dotnet test ... --no-restore --nologo`, build 포함). 이미지 대입/Ready 알림, Qualities/projection/thumbnail placeholder 알림에서 URL을 바꿔도 Empty snapshot을 유지한다.
- 실제 remote 성공/정상 종료 관찰은 이 guard 보완 전 `9989a31` 빌드에서 수행했다. 최종 코드의 원격/화면 재확인 및 전체 회귀는 아래 후속 검증에 구별해 기록한다.

## 단위 4 검증 보강 — 최소 크기·문자열·렌더 DPI

- 실제 STA Window.Show를 620×480으로 실행해 장문 한글/줄바꿈/emoji 제목과 장문 업로더, 30시간 길이, rational FPS, 펼친 진단의 readonly/wrap/내부 스크롤·전체 선택·접근성 Name·binding 오류 부재·가로 폭 제한을 검사했다. 96/144/192 DPI WPF RenderTargetBitmap을 상단/중간/하단에 생성해 시각 확인했다. bitmap은 ignored test bin의 `phase3-layout`에 보존한다.
- 최초 3개 layout 테스트는 접힌 Expander의 진단 TextBox가 visual tree에 아직 없어 6개 대신 5개로 실패했다. 테스트에서 진단을 펼쳐 layout 갱신 후 검사하도록 수정했다. 투명 배경의 첫 렌더가 검정으로 보이는 문제도 검증 renderer에 흰 배경과 명시 viewbox를 적용해 수정했다. 제품 XAML을 이 때문에 변경하지 않았다.
- WPF 전체 75통과·실패/skip 0. 렌더 viewbox 보완 후 관련 layout 3개를 재실행해 통과했다. 완료 상태/양수 duration gate 및 Ready+선택 실패의 Prepare 차단 검사도 추가했다.
- `VisualTreeHelper.GetDpi(window).PixelsPerInchX` 실제 관찰값은 144(150%). 96/192 렌더는 OS/모니터의 실제 100%/200% 전환 관찰과 다르다. 해당 native DPI 관찰은 미완료이며 Phase 완료/Passed로 표시하지 않는다. OS 배율 설정은 변경하지 않았다.

## 단위 4 보완 — 실패 알림·UI 테스트 공유 자원

- 일반 실패 진단/Failed 상태의 PropertyChanged에서도 URL 변경을 재검사한다. 취소 표시도 동일 current guard로 처리하며 안전상 자원 경고 누적은 그대로 유지한다. 새 실패 알림 재진입 2개를 추가했다.
- 병렬 실행 77개 중 기존 WindowClosing 1개가 `PackagePart.CleanUpRequestedStreamsList` → `Application.LoadComponent`의 NullReferenceException으로 실패했다. 별도 STA라도 WPF pack-resource cache/데스크톱 focus가 같은 프로세스에서 공유되는 환경임을 고려해 이 WPF 테스트 어셈블리만 직렬 실행한다. 제품은 단일 UI Dispatcher를 사용한다. 이는 실패를 삭제하거나 창 닫기 검사를 생략하는 변경이 아니다.
- 최초 설정은 설치된 xUnit 4에서 obsolete error(CS0619)를 낸 CollectionBehavior 속성이었다. 패키지의 로컬 XML API를 확인해 `Xunit.v3.Parallelization(Mode = ParallelMode.None)`으로 수정했다. 패키지 버전 변경 없음.
- 수정 후 `dotnet test tests/ClipDownloader.Tests/ClipDownloader.Tests.csproj --no-restore --nologo`: 77통과·실패/skip 0, 31초. 해당 build에서 경고/오류 없음. 느려진 실행을 수용하고 실제 Window·이미지 취소/종료·재진입 검사를 모두 유지했다.
