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

단위 3~4 구현·검증은 아직 수행하지 않았다. 새 패키지·도구 설치·원격 push·기존 태그 변경 없음.

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
