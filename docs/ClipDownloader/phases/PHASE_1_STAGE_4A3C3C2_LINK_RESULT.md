# Phase 1 4a-3c-3c-2 동일 파일의 container/probe 연결

실행일: 2026-10-13 (Asia/Seoul). 시작 HEAD `b687dd2aa0f8e69fda2bba44559c0637eaded851`, branch `phase/1`, clean. [MP4 reader](PHASE_1_STAGE_4A3C3C1_CONTAINER_RESULT.md) 커밋을 확인했다. Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **동일 수집 파일의 hash/read lock 안에서 container·FFprobe timing 연결 구현/관련 검증 완료**. 실제 로컬 FFprobe/ProcessRunner 경로도 한 기존 초기 참조의 복사본으로 확인했다. codec/fragment/native 원점·source inspector·target acquisition/4a 전체 완료는 아니다.

## 구현·확인 경계

- InitialReferenceProbe의 prehash 다음에 같은 열린 FileStream으로 MP4 timing을 읽고, version/FFprobe와 posthash까지 read-only share lock을 유지한다. container 오류는 process 실행 전에 반환한다. 파일 생성/삭제/cleanup 책임은 바꾸지 않았다.
- FFprobe stream 필드에 explicit `id`를 요청한다. internal stream 관찰은 optional positive UInt32 TrackId를 보존한다. numeric/decimal-string/FFprobe `0x` hex-string을 해석하며0·negative·overflow·null·malformed를 거절한다. 필드 누락은 parser에서 null이고 연결 gate에서는 실패다. index+1/list order fallback은 없다.
- container track와 probe stream 수가 같고, 각 stream의 explicit ID가 유일하며 해당 track에 존재하고, exact normalized time_base가1/mediaTimescale이어야 링크를 반환한다. ID/수/clock의 미확인·불일치는 VerificationFailed/Data=null이다. 순서가 다른 index8→track9/index2→track7 사례와2/12000=1/6000 정규화 대조도 확인했다.
- 성공 결과에는 원래 collected reference·probe version·Demux 관찰·container timing·read-only explicit links가 함께 있다. track handler/codec/fragment sample table을 이번에 검사하지 않았으므로 role/native semantics까지 확인했다는 뜻은 아니다. source 원점/FPS/CFR/coverage/decoder 준비에 대한 Verified fact는 만들지 않는다.
- 안전한 오류 Field로 ReferenceIdentity/ByteIdentityBefore/After·FileOpen/FileRead·ProbeExecution·TrackCount/TrackId/TrackClock을 구분한다. path/URI/raw output/exception message는 노출하지 않는다. runner I/O 예외는 ProcessFailed/ProbeExecution이며 파일 읽기/identity 실패와 구분한다. 확인된 termination failure의 기존 우선순위는 유지한다.

## 실제 검증

| 실행 | 결과 |
|---|---|
| Downloader Infrastructure tests `--no-restore` | 최종 단독 실행 exit0, 통과452/실패0/건너뜀0 |
| `dotnet build ClipMaker.slnx --no-restore` | exit0, 경고0/오류0 |
| 실제 FFprobe8.1 + ToolLocator/ProcessRunner + InitialReferenceProbe | sandbox 밖 실행 Succeeded,213 frames·2 links |

새20개 테스트 사례를 추가했다. 이전 fake process tests의 tiny bytes를 synthetic timing header 파일로 바꿔 새 container 경계도 거치게 했다. ID 해석/상한·missing/foreign/duplicate/extra·clock 불일치·목록 순서·container-before-process 실패·runner I/O 안전한 분류를 검사한다. 이전 hash/취소/lock/종료/출력 제한 테스트도 통과했다.

452개 테스트와 솔루션 빌드를 동시에 실행했을 때 test apphost 복사 MSB3026 재시도 경고가1회 발생했다. 둘 다 exit0였고 테스트를 단독 재실행해 경고 없는452개 통과를 확인했다. 이는 빌드 산출물 동시 접근 경고이며 media 검증 실패가 아니다. Core/Media.Infrastructure 테스트 전체는 합산하지 않았고 WPF는 빌드만 검사했다.

실제 로컬 검증은 기존 ignored G2 c1080-fractional/initial.bin을 새 고유 `.artifacts/initial-probe-real/<GUID>/initial.mp4`로 Copy/overwrite=false하여 별도 검증 파일을 소유하고 원본을 보존했다. 준비된 local fixture의 reference·hash/range/provenance는 reflection harness로 구성했다. **실제 HTTP collector/selected inspection/service 전체를 실행한 것은 아니다.** 검증 range/format label은 fixture 입력이며 source 범위/선택 지원 증명이 아니다. 새 프로젝트·설치·network·다운로드는 없다.

| 실제 관찰 | 값 |
|---|---|
| 파일 bytes / movie timescale |2,050,886 /6000 |
| probe version |8.1-full_build-www.gyan.dev |
| video stream index→track ID /media timescale |0→1 /6000 |
| audio stream index→track ID /media timescale |1→2 /48000 |
| edit list |양 track 없음 |
| decoded frame 수 |213 |

최초 실제 harness는 sandbox 안에서 VerificationFailed를 반환했다. container와 별도 CLI의 ID/clock은 일치했다. 안전한 단계 진단으로 ProbeExecution I/O 실패를 확인하고 sandbox 밖에서 같은 파일/harness를 재실행해 성공했다. 구체적 OS I/O 원인은 확정하지 않았으며 이 실패를 native clock 모순으로 기록하지 않는다. 실패 진단을 제품의 안전한 Field로 보존하고 runner I/O 분류 테스트를 추가했다. 검증 복사본과 기존 실험 자료는 보존한다.

## 다음 단위·커밋

다음 **4a-3c-3c-3**는 codec/fragment timing 및 edit·priming의 native evidence/미확인 gate다. edit 부재·연결 성공 자체를 native correction0/source O로 승격하지 않는다. 실제 초기 참조의 원점/FPS fact·selected inspector와 target acquisition 연결은 아직 남아 있다. Phase2·WPF 다운로드 UI·ClipEditor는 진행하지 않는다.

이번 커밋 범위는 Infrastructure 기존3·test 기존2·결과/설계/source 계약/baseline4문서다. staging/commit은 하지 않았다. 권장 메시지: `feat: Link MP4 timing to explicit probe track identities`.
