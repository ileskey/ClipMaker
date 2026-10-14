# Phase 1 후속 — YouTube AAC 시작 skip 진단

2026-10-14. 시작 HEAD `517068b`, branch `phase/1`, clean에서 사용자가 보고한 `wFOjWCLPDDM` Fast 실행 실패를 진단했다. 요청은 `00:00:00:00`부터 `00:00:14:24`, Best가 선택한137/140, 선언 FPS25/1이다. 준비까지 통과했으며 시간 입력 자체의 오류로 확인되지 않았다.

## 실제 원인과 지원 상태

기존 도구·32 MiB/30초 budget에서 같은 영상/선택의 초기 indexed 참조만 수집해 `NativeSampleCompleteness`를 재현했다. source 초기 검사 실패이므로 Fast copy/출력 검증/최종 확정까지 도달하지 않았다. 재현은 최종 clip을 만드는 실험과 구별한다.

| 원본 초기 참조 | native sample | demux packet | decoded frame | 공유 native gate |
|---|---:|---:|---:|---|
| 영상137 | 175 | 175 | 175 | 성공 |
| 음성140 | 430 | 430 | 429 | NativeSampleCompleteness |

음성 track은44100Hz, 단일 rate1 edit의 MediaTime1600이었다. 첫 packet PTS/DTS는-1600, duration1024, SkipSamples1600/DiscardPadding0/각reason0이다. 다음 packet은-576이고 최초 decoded frame은PTS0/duration·nb_samples448이었다. 시작 보정1600이 첫1024 sample packet 전체와 다음 packet의576 sample을 소비해 native packet/sample과 decoded frame이 일대일이 아니게 된다. 음성 초기 파일의 공유 FFprobe 검사 자체는 성공했고 decoder 오류로 실패하지 않았다. 단순한 frame 개수 차이를 손상 확정으로 설명하지 않는다.

현재 `Mp4NativeObservationVerifier`는 nonzero skip/padding을 `NativeSkipUnsupported`로 명시 거절하는 제한된 전략이다. 그러나 frame 수 완전성 검사가 skip 확인보다 앞에 있어 이 사례를 불완전 데이터로 분류했다. 같은 helper를 indexed와HLS가 공유하는데 오류 문구에 HLS를 고정한 점도 혼동을 줬다.

**이 영상은 여전히 현재 source 검사에서 미지원이다.** 이번 변경은 오류 분류/안내를 보완하며 AAC 시작 보정 지원이나 실제 clip 성공을 추가하지 않는다. 해당 검사 경로는 source inspection의 공통 gate이므로 모드 변경을 자동 대안으로 사용하지 않는다. 이 영상 Accurate 실제 실행은 수행하지 않았다. 일반 시간축·정확도·보정 정책을 재승인받거나 검사 무시·tolerance·전체 다운로드 fallback을 넣지 않았다.

## 수정 범위

- native sample↔packet 수 완전성은 먼저 검사한다. 기존 packet identity/timing/state/skip 검사를 수행한 뒤 decoded frame 수와 일대일 frame 검사를 한다. nonzero skip은 UnsupportedInput, skip 필드 누락과 skip 없이 frame 누락은 InvalidMedia로 유지한다. 성공 허용 조건은 완화하지 않는다.
- Downloader는 UnsupportedInput을 UnsupportedFormat으로 전달하고, native skip의 시작/끝 음성 보정 미지원 안내를 표시한다. generic 원본 오류의 HLS 고정 문구를 제거했다.
- Completed progress는 최종 결과 반환 단계이므로 `진행 중`/100% 대신 `결과 반환 중`을 표시한다. 실패/취소도 이 단계를 거치며 최종 ClipJobResult만 성공을 결정한다.

AAC1600 skip·잘린 frame, skip 정보 누락, skip 없는 frame 누락, packet 누락의4개 shared gate 회귀와 Downloader 오류 매핑·CLI Completed 표시2개를 추가했다.

## 검증과 인계

실제 CLI를 같은 URL/선택/시간과 --yes, 고유 ignored output으로 재실행했다. exit1, `UnsupportedFormat/Execution` 및 `NativeSkipUnsupported`, `[Completed] 결과 반환 중`, final 없음과 실제 app-data의 해당 job folder0을 확인했다. 사용자 `out.mp4`를 생성·덮어쓰기·삭제하지 않았다. 초기 진단 파일은 ignored `.artifacts/reported-source/d5241b0f7d8748d5b7174615fc28cebc`에 증거로 보존한다. Application cleanup 이후 지워진 파일을 추정해서 검사하지 않는다.

[안전한 증거](evidence/PHASE_1_YOUTUBE_AAC_SKIP_DIAGNOSIS.json)는 metadata/소유 초기 파일 hash·track/sample/packet/frame 관찰과 수정 후 CLI 결과다. signed주소/인증값/원시metadataJSON은 없다. 실제 로그의 처음 조회에는 기존6단계 검증 이력만 있어 사용자 실행의 작업 ID를 특정하지 못했다. 사용자 출력·동일 source 재현·수정 후 실제 CLI를 근거로 한다.

최초 sandbox solution build는 오류 진단 없이 exit1이어서 성공으로 세지 않았다. 정상 실행 환경에서 `dotnet build ClipMaker.slnx --no-restore`가 경고/오류0으로 통과했다. 같은 빌드의 각 test 프로젝트를 --no-restore --no-build로 실행해 Core143/Media Infrastructure147/Downloader837, 총1127개 실패/skip0을 확인했다. 기존 성공 네 clip의 다운로드/재생을 반복한 결과로 기록하지 않는다.

다음 지원 보완은 source edit/skip으로 완전히 사라진 packet과 부분 frame의 정확한 대응, 실제 유효 audio support·source 원점/공통축·provider 참조 관계, Fast 복사와 Accurate source-start decode/output 검증까지 영향을 확인해야 한다. frame 개수 검사만 제거하거나1600을 임의 clamp하는 변경으로 해결했다고 판단할 수 없다. Phase1 전체 완료/Passed/Completion/Tag와 Phase2 시작은 계속 보류한다.
