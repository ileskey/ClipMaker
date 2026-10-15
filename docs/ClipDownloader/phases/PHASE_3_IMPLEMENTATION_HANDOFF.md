# ClipDownloader Phase 3 구현·검증 context 인계

작성일: 2026-10-15 (Asia/Seoul). 대상은 **Phase 3 — Metadata UI 구현 및 검증**이다. 이 인계 작성 context에서는 제품 구현이나 다운로드를 시작하지 않았다.

2026-10-16 후속 결과: 구현·필수 검증을 완료했다. 아래는 착수 전 인계 기록이며 현재 상태는 [실제 구현·검증 기록](PHASE_3_IMPLEMENTATION.md), [설계 결과 10절](PHASE_3_DESIGN.md#10-구현-후-결과현재-인계-2026-10-16), [Baseline](../PHASE_BASELINE.md)을 따른다. 원본 사본은 ignored `.artifacts/phase3-verification/inputs`에 보존했다.

## 1. 이번 context에서 확정한 사항

- 상세 설계는 [PHASE_3_DESIGN.md](PHASE_3_DESIGN.md)다. 기존 [PHASE_3_DESIGN_HANDOFF.md](PHASE_3_DESIGN_HANDOFF.md)는 설계 시작 전 자료이므로 설계 결정은 상세 설계를 따른다.
- 사용자가 후속 지시로 **`phase/3` 브랜치 사용**을 지정했다. 다른 이름의 브랜치를 만들 필요가 없다.
- 당장 추가 사용자 결정을 요구하는 항목은 없다. 주소 정책 세부화·정상 이미지 수용성·한도 검증은 구현 단위 안에서 진행한다. 새 근거로 계약 변경이 필요해지면 영향과 trade-off를 기록한다.
- 후속 구현은 논리 단위마다 **구현 → 실제 관련 검증 → 결과·제약 보고 → 해당 단위 커밋**으로 진행한다. 마지막에 커밋을 몰아 만들고 당시 존재한 것처럼 소급 기록하지 않는다.

## 2. 인계 작성 시 실제 Git 상태

| 항목 | 확인한 값 |
|---|---|
| branch / HEAD | `phase/3` / `5d5a06e6a6d0b8ec8ea952e738ecf99756a53712` |
| Phase 2 Completion / tag | `b4ef12692da9268c72c2a42e6c0ab73351d7bb9f` / `phase-2-complete` dereference 일치 |
| 검증된 제품 소스 | `86fd9f8014aba1135fe30ba6d5b14be4df3ab152` |
| Completion 이후 committed diff | docs·증거 7파일, +139/−10. 상세 내역은 설계 1절과 Git에서 확인 |
| 이 파일 작성 직전 미커밋 상태 | `?? docs/ClipDownloader/phases/PHASE_3_DESIGN.md` 한 파일. staged/unstaged tracked 변경 없음 |
| Phase 3 Base | 후보는 위 HEAD. 아직 Baseline에 확정 기록하지 않음 |

새 context에서는 branch·HEAD·tag·전체 staged/unstaged/untracked 변경과 Completion 이후 제품 diff를 다시 확인한다. 최신 상태가 이 표와 다르면 사용자 변경을 보존하고 차이를 기록한다. 현재 HEAD가 그대로이고 추가 제품 변경이 없으면 이 HEAD를 Phase 3 Base로 확정하고 [Baseline](../PHASE_BASELINE.md)에 In Progress / Pending으로 기록할 수 있다. 설계 작성만으로 Completion/Passed를 만들지 않는다.

**상세 설계와 이 인계는 아직 미커밋 파일이다.** 같은 checkout에서는 파일을 그대로 읽을 수 있지만 HEAD만 가져오는 별도 checkout에는 포함되지 않는다. 인계받은 실제 파일의 존재부터 확인하고 현재 HEAD에 포함됐다고 가정하지 않는다. 문서 전달을 위해 이번 context에서 임의 commit은 하지 않는다. ignored `.artifacts` 및 사용자 미디어·검증 산출물을 삭제하지 않는다. 기존 완료 태그 이동·history 재작성·원격 push는 하지 않는다.

## 3. 읽을 순서와 실제 연결 지점

1. 사용자 지시·적용 AGENTS.md, 이 문서, [Phase 3 상세 설계](PHASE_3_DESIGN.md).
2. [운영 규칙](../PHASE_DESIGN.md), [Baseline](../PHASE_BASELINE.md), 설계 1절의 요구사항·아키텍처·구조·작업 지침.
3. [Phase 2 실제 구현·검증](PHASE_2_IMPLEMENTATION.md), [상세 설계](PHASE_2_DESIGN.md), [미디어 증거](evidence/PHASE_2_WPF_MEDIA.json). 지원 경계는 [Phase 1 최종 인계](PHASE_1_FINAL_VERIFICATION_HANDOFF.md)도 확인한다.
4. 설계 2절의 현재 source와 테스트를 직접 읽는다. 특히 `MainViewModel.cs`, `.Input.cs`, `.Job.cs`, `MainWindow.xaml(.cs)`, `App.xaml.cs`, `Composition/ServiceRegistration.cs`, `VideoInfo`, `FormatSelection`, `Validation/FormatSelector.cs`, `YtDlpMetadataParser.cs`다.

과거 문서의 미구현 설명과 예시 API는 현재 소스를 대체하지 않는다. Phase 2의 1,230개 통과·실제 네 클립·사용자 재생 확인은 기존 근거이며 Phase 3 테스트/실제 이미지 검증을 대신하지 않는다. 이번 설계 context에서는 build/test·WPF·metadata/thumbnail/미디어 획득을 실행하지 않았다.

## 4. 구현 시 유지할 핵심 결정

### 표시·선택

Title·Uploader·Duration·FPS·Thumbnail·Available resolutions 모두 대상이다. nullable 필드는 미확인/없음으로 설명한다. Ready는 Completed·양수 duration gate를 유지한다. 조회 실패·URL 변경·동일 URL 재조회 시작 시 이전 필드/선택/이미지를 즉시 제거한다. 성공 시 Start/End=0, Quality=Best로 초기화하고 Output/Mode는 유지한다.

대표 FPS는 만들지 않는다. 선택한 `VideoFormat.FrameRate`의 rational을 표시하고 decimal 근사는 보조로만 사용한다. FPS null의 pending 검사·`PreparedClip.Range=null` 경로를 보존한다. 해상도는 기존 `VideoStream != Absent && Height > 0`의 distinct 높이 내림차순이며 Quality와 같은 helper를 사용한다. Best는 해상도가 아니다. metadata Ready와 선택 성공은 구분한다. Issues의 FormatIndex를 필터 후 Formats 인덱스로 해석하지 않는다.

### 썸네일

설계 4절의 Application bytes 계약 → Infrastructure 제한 HTTP → WPF decode 경계를 사용한다. 기존 URI parser 정책을 유지하고 query 제거·redirect 추적·다른 URL 추측으로 우회하지 않는다. HTTP는 HTTPS 443·public 목적지 연결 검증·redirect 금지·전용 무인증/무쿠키/무proxy·HTTP/1.1 exact·JPEG/PNG·4 MiB·전체 10초 제한이다. 실제 연결에 검증한 IP만 쓰고 TLS hostname 검증은 유지한다.

주소 차단표와 DNS/connector 테스트는 HTTP 구현 단위에서 구체화한다. 무제한 기본 HTTP로 대체하지 않는다. 이미지 preflight는 각 축 4096·총 8 MP 이하, decode 목표 640×360, OnLoad/Freeze·stream 해제를 사용한다. in-process WPF decode의 강제 중단·전체 메모리 상한을 보장한다고 주장하지 않는다. 상세 수치·reason code·지원 형식·동시 실행 정책은 설계 원문이 기준이다.

### 비동기·취소·종료

썸네일 상태/Task/CTS/epoch는 foreground 작업과 독립이다. 이미지 Loading/실패/취소는 유효한 Prepare를 막지 않는다. 기존 작업 generation을 이미지에 재사용하지 않는다. metadataRevision·epoch·slot identity·token·수명 guard로 늦은 응답을 차단하고 이전 이미지 작업의 실제 종료 뒤 새 획득/decode를 직렬 실행한다. 이미지 취소는 전용 명령이며 재시도는 전체 영상 재조회다.

오래된 metadata의 화면 갱신을 막더라도 ProcessTerminationFailed·잔존 자원 경고 관찰/누적은 유지한다. 취소 요청만으로 종료를 선언하지 않는다. Shutdown은 foreground/path/이미지 작업과 정리를 모두 await하고 경고 확인 버튼도 이미지 완료 전에 활성화하지 않는다. 기존 Dispatcher 지연 Close → DI → 종료 로그 → logger/SelfLog 순서를 유지한다.

### 실행·지원 경계

PreparedClip 동일 객체 한 번 실행, 모든 실행 입력 변경 시 준비 무효화, 최종 ClipJobResult로만 성공 판정, 선택/미디어 검증 gate와 원본 획득 64 MiB/60초·playlist 2 MiB·계획 segment 64개를 보존한다. SourceEndVerified=false·codec/clock 제약, 일부 CHZZK MPD metadata 실패·먼 위치 Accurate 제한은 기존 경계다. 자동 quality/mode 변경·전체 다운로드 fallback·Queue·Player·ClipEditor는 추가하지 않는다.

## 5. 실행·검증·완료 기록

구현 순서는 설계 6절의 단위 0 Git 준비 → 1 텍스트/선택 projection → 2 HTTP 계약·정책 → 3 decode/수명/DI/UI → 4 실제 검증·기록이다. 각 단위를 build 가능한 변경으로 검증·보고·커밋한다. 새 패키지는 계획하지 않는다.

설계 7절의 결정론적 HTTP·decode·fake/STA/Dispatcher·실제 Window.Close·기존 Prepare/Execute 회귀를 수행한다. 솔루션 build와 기존 네 test 프로젝트의 전체 회귀를 실제 실행하고 수치·명령·실패/수정·미확인을 기록한다. 실제 WPF에서 양 사이트 metadata와 원격 thumbnail 표시·수용성·접근성·취소·종료를 관찰한다. 정책 거절을 정상 이미지 표시 성공으로 세지 않는다. 정상 사례를 확보하지 못하면 완료 판단 전에 미해결로 보고한다.

현재 제한 수치의 실제 이미지 적합성, custom connect/TLS와 주소 분류, decode 비용은 미검증이다. 구현 과정에서 확인하며 임의 정책 완화로 숨기지 않는다. fake 관찰·실제 원격 관찰·사용자 재생 확인의 증거를 구분한다.

미디어 생성 재검증은 설계 7.3절의 영향 기준을 따른다. 미디어 계약 무변경이면 Phase 2 네 클립 증거를 유지하고 새 생성했다고 주장하지 않는다. selector·시간/FPS 변환·Prepare/Execute·media/source DI·예산·종료 정리에 영향이 있으면 관련 실제 생성·decode·A/V/취소/자원 검증을 다시 수행한다. metadata/thumbnail 획득과 미디어 다운로드를 구분한다.

실제 결과는 `PHASE_3_IMPLEMENTATION.md`에 작성하고 설계의 변경 결정/결과 및 Baseline을 필요한 시점에 정합화한다. 필수 실제 검증을 못 했으면 완료/Passed로 표시하지 않는다. 설계 변경은 이유·영향·추가 검증을 기록한다.

## 6. 새 context 시작 프롬프트

> 이 context에서는 ClipDownloader Phase 3 — Metadata UI 구현 및 검증을 진행해줘. 먼저 `docs/ClipDownloader/phases/PHASE_3_IMPLEMENTATION_HANDOFF.md`와 `PHASE_3_DESIGN.md`, 연결된 운영 규칙·Baseline·요구사항·Phase 2 실제 구현/검증 기록을 읽고 현재 source와 대조해줘. 브랜치는 사용자 지정 `phase/3`을 사용해. 인계 시 HEAD는 `5d5a06e6a6d0b8ec8ea952e738ecf99756a53712`, Phase 2 Completion은 `b4ef12692da9268c72c2a42e6c0ab73351d7bb9f`, 태그는 `phase-2-complete`, 검증 제품 소스는 `86fd9f8014aba1135fe30ba6d5b14be4df3ab152`야. 시작 시 branch/HEAD/tag와 staged·unstaged·untracked 변경, Completion 이후 제품 diff를 다시 확인하고 사용자 파일·미커밋 설계/인계·`.artifacts`를 보존해줘. 실제 상태를 대조해 Phase 3 Base를 확정하고 Baseline에 기록해줘. 설계 6~7절의 논리 단위마다 구현→관련 검증→결과/제약 보고→해당 단위 커밋을 진행해줘. 여섯 metadata 필드·선택 FPS/해상도·제한 thumbnail HTTP/decode·취소/stale/종료 수명을 구현하되 대표 FPS 가정이나 기존 URI 정책의 조용한 완화는 금지야. HTTP 주소 차단표·연결/TLS와 실제 양 사이트 thumbnail 수용성은 단위 안에서 구체화·검증해줘. PreparedClip 한 번 실행·입력 변경 시 준비 무효화·최종 반환 결과 기준 성공·정리 후 DI/logger 해제·FPS 미확인 경로·기존 원본 예산과 지원 경계를 보존해줘. Queue·Player·ClipEditor·자동 도구 설치는 제외야. 새 패키지 없이 구현하고, 실제 metadata/thumbnail 검증과 기존 회귀를 수행해줘. 미디어 생성 재검증 범위는 설계 7.3절의 영향 기준을 적용해줘. fake/실제 관찰/기존 증거를 구분해 `PHASE_3_IMPLEMENTATION.md`에 결과를 기록하고, 필수 검증 미완료를 Passed로 표시하지 마. 설계 변경이 필요하면 근거·trade-off·검증 영향을 기록해줘.

## 7. 이 인계의 확인 범위

Git 상태·설계 최종 결정·사용자 branch 지정을 재대조했다. 이 인계는 제품 구현·테스트 실행·다운로드·branch/commit/tag 생성·원격 push를 수행하지 않는다. 문서 링크·공백과 최종 변경 범위만 검사한다.
