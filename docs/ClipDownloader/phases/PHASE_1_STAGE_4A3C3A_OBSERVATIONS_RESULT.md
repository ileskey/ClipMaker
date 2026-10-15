# Phase 1 4a-3c-3a 초기 참조 관찰 파서

실행일: 2026-10-13 (Asia/Seoul). 시작 HEAD `a609ee0b909d8a5861cc15c614f4f9800e4e961e`, branch `phase/1`, clean. Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **초기 참조의 bounded FFprobe JSON 관찰 모델/파서 구현·관련 검증 완료**. [4a-3c-2](PHASE_1_STAGE_4A3C2_COLLECTION_RESULT.md) 다음 native 관찰 연결을 논리적 커밋으로 나눈 첫 단위다. FFprobe 실행·수집 파일 identity/hash 결합·native 의미 판정·source 원점/FPS fact 발행은 아직 구현하지 않았다. 전체 준비/구현 완료가 아니다.

## 관찰 경계

Infrastructure internal `InitialReferenceObservations`와 `InitialReferenceObservationParser`를 추가했다. public inspection/MediaProbe API는 추가하지 않는다. 목록은 복사한 read-only 값이며 JSON 수명 종료 뒤에도 사용할 수 있다.

- stream index/역할/양수 exact time_base, optional signed Int64 start_pts, 선언 r_frame_rate/평균 avg_frame_rate를 보존한다. 두 rate는 별도 값이며 같아도 CFR 증명이 아니다. 누락과 FFprobe의 `0/0` rate는 미상이다. 잘못된 rate/time_base는 거절한다.
- decoded frame의 stream index/역할, optional signed Int64 pts, optional 비음수 duration tick, audio의 optional 양수 Int32 nb_samples를 보존한다. start_pts와 frame pts는 별도 관찰이다. PTS가 빠지면 null이며 DTS/best_effort/start_pts/0으로 대체하지 않는다.
- TimeBasis는 **Demux**다. decoder 출력/stream 시작이 native 원점 O나 source 전체 첫 sample이라는 판정을 하지 않는다. movie/edit/priming 보정과 native container 대조는 후속 단위다. native end·source coverage·decoder 준비·audio absence·지원 성공을 발행하지 않는다.
- strict UTF-8/JSON depth32/8MiB/최대16 streams/100,000 frames로 제한한다. streams/frames 배열은 필수이며 frames 빈 목록은 빈 관찰이다. 중복 root/stream/frame 필드, stream index 중복, 누락/foreign stream 연결, 역할 불일치, overflow·fractional PTS, 잘못된 선택 필드는 안전한 InvalidMetadata/Data=null로 거절한다. video/audio 외 역할은 현재 지원 범위 밖이다.
- codec/profile/pixel format/sample rate, format.start_time, skip samples/edit 정보는 이번 모델에 없다. 해당 정보와 실제 native/container 검사가 필요한 후속 판정에서 이 모델만으로 충분하다고 간주해서는 안 된다. 입력 schema는 이번 제한 경로용이며 모든 FFprobe 출력에 대한 범용 파서가 아니다.

## 실제 검증

| 실행 | 결과 |
|---|---|
| `dotnet test tests/ClipDownloader.Infrastructure.Tests/ClipDownloader.Infrastructure.Tests.csproj --no-restore` | exit0, 통과404/실패0/건너뜀0 |
| `dotnet build ClipMaker.slnx --no-restore` | exit0, 경고0/오류0 |

새19개 사례는 exact signed time/rational FPS·A/V 관찰·read-only 목록·미상값/대체 차단·중복/오류/크기/frame count 제한을 검사한다. synthetic JSON 검증이다. 실제 파일/FFprobe/외부 network/영상 다운로드/설치 작업은 실행하지 않았다. Core/Media.Infrastructure 테스트 전체는 합산하지 않았고 WPF는 빌드만 검사했다.

## 다음 단위와 커밋

다음 **4a-3c-3b**는 local FFprobe 실행/수명/출력 제한과 수집 파일 byte/hash identity 연결이다. 그 다음 container/native 보정·초기 원점/FPS evidence와 selected inspection 연결을 검증한다. playlist 좌표0과 stream.start_pts를 source O로 자동 채택하지 않는다. 기존 사이트/포맷 실험 자료를 보존하며 실제 bounded source 검증은 해당 경로 연결 뒤 진행한다.

이번 범위는 신규 Infrastructure2·test1·결과문서1과 설계/source 계약/baseline 갱신3파일이다. staging/commit은 하지 않았다. 권장 메시지: `feat: Parse bounded initial reference observations`.
