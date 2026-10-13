# Phase 1 source 시간 기준·mapping 하위 계약안

작성일: 2026-10-11 (Asia/Seoul). 시작 HEAD `481958d790c44bdd4d28356ced1d3ae6fb59540f`, branch `phase/1`, 작업 트리 clean. Phase 1 Base는 `0759dc52475538df848f765ce30c005cf755107f`다.

상태: **사용자 시간 의미 A(선택 포맷 기준) 채택 완료, 구체 측정·mapping·오류 확장안은 검증/통합 대기**. 2026-10-11 사용자가 “선택 포맷 기준으로 진행하자”라고 결정했다. 채택은 시간 0의 의미에 한정하며 이 문서 전체·실험 의존 알고리즘·4단계 준비 완료를 일괄 확정하지 않는다. 제품 코드와 공개 API를 변경하지 않았다.

## 1. 요구사항과 기존 해석의 정리

[요구사항 §3.3~3.4](../requirements/01_PRODUCT_REQUIREMENTS.md)는 실제 영상 FPS에 따른 타임코드, IN/OUT, Fast의 keyframe 제약, Accurate의 프레임 정확도를 요구한다. [채택 설계 §4.1·4.3·4.25·4.32](PHASE_1_DESIGN.md)는 선택 video format의 FPS, `[Start, End)`, 원본/로컬 관계의 검증, 미확인 Accurate 성공 금지와 단계별 검증을 추가로 명시한다.

**화질을 바꿔도 같은 숫자 IN이 반드시 같은 내용을 가리켜야 한다는 보증과 임의 browser 세션 시간의 정확한 재현은 현재 명시된 요구가 아니다.** 앞선 일반화/player 실험은 그런 추가 보증의 가능성을 조사했다. 그 결과 공통 원점을 확인하지 못했으며 선택 포맷 상대 시간을 품질 독립 원점으로 설명하면 틀리다는 반례를 얻었다. 그 반례를 선택 포맷 상대 시간이라는 정의 자체가 불가능하다는 결론으로 확장하지 않는다. 과거 실험 기록은 당시 범위·판정을 보존한다.

이미 채택한 시간 문법·FPS·구간·Y/N·실패/취소·계층·출력 확정·재시도 정책은 다시 승인받지 않는다. 사용자 시계 시간 0의 의미는 A로 채택했다. 그것을 구현·검증할 구체 경로는 남아 있다.

## 2. 새로 선택할 시간 의미와 권고

| 선택 | 사용자 IN의 의미 | 장점 | 비용·보증 한계 |
|---|---|---|---|
| A. 선택 포맷 기준 — 사용자 채택 완료 | 선택한 원본 미디어의 확인된 입력 시작을 0으로 한 재생 시간 | 현재 선택 FPS/포맷 계약과 연결되며 이미 측정한 source/local map·trim을 활용 가능 | 품질 변경 시 동일 내용의 시간이 달라질 수 있음. 임의 사이트 player 세션과 일치 보장 없음 |
| B. 품질 독립 source 기준 | 출력 품질과 별개로 고정한 source timeline의 시간 | 같은 내용 기준의 품질 간 비교 가능 | 기준 timeline 선택 근거·포맷 간 대응·갱신 규칙이 추가로 필요. 현재 권위 있는 기준 미확정 |
| C. 사이트 player 세션 기준 | 특정 player 세션이 실제 보여주는 시간 | 그 세션의 타임스탬프를 그대로 쓰는 목표에 적합 | URL 외 초기 offset/track/reset 맥락 필요. 현재 실제 runtime mapping 직접 검증 불가 |

**A를 권고한다.** 현재 명시 요구를 충족하는 가장 작은 범위이며, 불명확한 품질 독립 원점을 찾기 위해 Phase 1에 추가 source-reference 기능을 넣지 않는다. 이는 일반 정책 재검토가 아니라 미정인 원점 의미의 제안이다. 사용자가 같은 숫자 IN의 품질 간 내용 일치나 사이트 player 표시와의 정밀 일치를 원하면 B/C를 요구로 명시하고 해당 선행 작업을 계획한다. A를 선택해도 원본/로컬 mapping·coverage·정확도 검증을 생략하지 않는다.

A의 준비 요약에는 사용자에게 의미가 있는 제한인 “시간은 선택 원본 포맷 기준이며 사이트 player·다른 화질과 다를 수 있음”을 표시하는 안이다. PTS·clock·내부 도구 인자를 제품 안내에 나열하지 않는다. 새 `--time-origin` 옵션이나 모드는 추가하지 않는다. 사용자가 A의 시간 의미를 채택했다. 안내 구현과 원점 측정·mapping 보증은 후속 검증·구현 대상이다.

## 3. 채택한 A의 구체 계약안 — 실험 의존 항목은 검증 대기

### 3.1 세 좌표와 원점

| 좌표 | 의미 | 용도 |
|---|---|---|
| U | 사용자 선택 원본의 상대 재생 시간 | Timecode·IN/OUT·동일 기준 Duration 검증 |
| S | 선택 원본의 실제 presentation timestamp | 원본 frame/sample 선택·획득 결과 대응 |
| Lᵢ | 획득 로컬 파일의 stream i presentation timestamp | 로컬 처리·검사 |

`S = U + O`, `S = Lᵢ + Bᵢ`, 따라서 `Lᵢ = U + O − Bᵢ`로 연결한다. 각 항은 실제 time_base를 적용한 signed 유리수 시간이며 정수 PTS를 부동소수 초로 반올림해 비교하지 않는다. 현재 Timecode/MediaTimeRange는 nonnegative 사용자 입력용이므로 negative local timestamp·offset 모델로 재사용하지 않는다. 공통 clock 48000·고정 2초·고정 Bᵢ는 제품 상수가 아니다. 기존 tick 반올림은 실제 TimeSpan 경계에서만 적용한다.

원점 O의 **검증할 첫 구현 후보**는 단일 선택 원본에서 첫 구간의 실제 stream start_pts/time_base를 확인한 뒤 존재하는 video/audio 중 더 이른 presentation 시작이다. 여기서 “시작”은 원본 전체의 확인된 시작이며, 부분 획득 파일의 첫 packet이나 중간 구간 probe가 반환한 우연한 시작이 아니다. ffprobe start_time 하나나 요청 section_start만으로 확정하지 않는다. 초기 fragment/stream 정보를 같이 읽는 구체 전략은 아래 검증 G1 대상이다. 후보의 정의를 채택하는 것과 도구가 그 정의를 정확히 측정한다는 실증을 구별한다.

이 후보는 edit list·skip samples·priming이 있는 다른 입력에도 단순 raw packet 최소값을 적용하자는 뜻이 아니다. 실제 presentation 시작의 의미와 도구가 반환한 stream 시작의 관계를 G1에서 확인한다. DTS 시작이나 container 숫자를 presentation 원점으로 조용히 대체하지 않는다.

음성이 실제 없는 원본이면 video 시작을 사용할 수 있다. 음성 유무 미확인은 음성 없음으로 대체하지 않는다. 분리 A/V 원본은 timestamp 축의 공통 관계를 먼저 확인해야 하며, 다른 파일에서 얻은 시작 값을 같은 축이라고 가정해 최소값을 취하지 않는다. 공통 관계가 없으면 그 조합의 Accurate 경로는 아직 지원 가능으로 확정하지 않는다.

처리 중 선택 포맷·asset generation이 바뀌면 이전 O/Bᵢ를 재사용하지 않는다. metadata 재조회·포맷 갱신은 기존 재시도 정책을 따르며 사용자 요청/원본 identity와 시간 관계를 다시 확인한다. 보조 검사 때문에 출력 화질을 바꾸지 않는다.

### 3.2 대응·coverage·정확도를 별도로 판정

| 판정 | 필요한 근거 | 이 근거만으로 보장하지 않는 것 |
|---|---|---|
| 원점 정의/확인 | 선택한 시간 의미와 실제 초기 presentation 기준 | 로컬 파일에 필요한 내용이 존재함 |
| stream mapping 확인 | source와 local의 내용 대응·정수 PTS/time_base에 일관된 Bᵢ | 전체 target coverage·재인코딩 정확도 |
| target coverage 확인 | `[Uin,Uout)`에 필요한 video frame·audio sample 경계를 획득 파일이 포함 | 출력 결과가 같은 집합/동기를 유지함 |
| 결과 정확도 확인 | 요청 target과 실제 staging의 PTS·frame/sample·끝 경계·A/V 관계 대조 | 최종 파일 확정 완료 |

이번 실험의 동일 packet payload hash·여러 anchor·일정 offset은 mapping을 확인하는 검증 수단이다. 두 anchor라는 숫자만으로 전체 구간을 증명하지 않는다. 같은 hash가 반복되면 timestamp와 독립 anchor로 대응을 좁히고, 유일하게 결정되지 않는 대응은 미확인으로 남긴다. payload가 변경/재인코딩되면 packet hash 방법으로 확인했다고 주장하지 않는다. discontinuity/offset 변화가 있으면 한 개의 Bᵢ로 덮지 않는다. Phase 1 첫 구현 후보는 검증된 단일 연속 구간이며 piecewise 범용 timeline 엔진을 선행 구현하지 않는다.

Accurate video의 선택은 presentation 시각 S가 `[O+Uin,O+Uout)`에 포함되는 원본 frame 집합이다. DTS를 PTS 대신 사용하지 않고 keyframe 시작과 첫 표시 frame을 구별한다. stream별 `STARTPTS`를 독립적으로 0으로 만들어 A/V offset을 없애지 않는다. 같은 사용자 target 기준으로 출력 시각을 이동한다.

audio는 target과 겹치는 packet을 먼저 확보하고 decode sample/presentation 경계로 자른다. AAC priming·padding·유효 끝을 구별한다. gap/overlap을 유지·보정·거절하는 최종 정책과 끝 경계 허용치는 아직 별도 실험 대상이다. 479999/480001 samples를 일률적으로 480000으로 맞추거나 고정 1 sample 허용 오차를 채택하지 않는다. Fast의 keyframe 편차는 별도로 기록하며 원점/stream mapping 불명확성을 keyframe 제한이라고 설명하지 않는다.

### 3.3 수명·계층·표현

다음은 구현 시 필요한 최소 의미이며 최종 CLR 이름/시그니처는 채택·G1 이후 통합한다.

- PreparedClip: 선택 source/format·사용자 범위·확인 가능한 메타데이터·시간 기준 설명·미확인 항목. 원격 media 획득 없이 mapping 확인을 주장하지 않는다.
- AcquiredMedia: 소유 파일·확인한 원점·stream별 source/local 관계·확인 범위·coverage/제한. unknown과 contradictory를 confirmed와 구별한다. 불변 snapshot으로 보존한다.
- MediaInput/MediaProcessingRequest: 로컬 파일·stream/time_base·변환한 로컬 범위·보존할 상대 시각. 원본 URL·사이트·format ID·인증정보는 공유 Media 계층에 넘기지 않는다.
- 같은 기준의 Duration: VideoInfo.Duration을 초기 입력 제한에 쓰는 기존 계약은 유지하되 O와 다른 축의 metadata 숫자가 정밀 media 끝을 입증한다고 보지 않는다. 부분 구간 coverage와 전체 길이 확인을 혼동하지 않는다. metadata 길이를 조용히 늘려 요청을 허용하지 않는다.

Media.Core의 signed 유리수 timestamp·일반 stream mapping 표현, Application의 source 기준 정보, Infrastructure의 실제 측정 책임을 분리한다. 실험 scalar evidence 전체나 원시 probe JSON을 공개 모델에 넣지 않는다. 기존 Timecode·FrameRate·MediaTimeRange·FormatSelector·OperationResult는 유지 가능하다.

## 4. 실행 gate·실패 분류안

확인 전 알려진 차단은 Y/N 전에 반환한다. 확인 후에만 측정할 수 있는 조건은 준비 결과에 미확인으로 표시하고 획득·입력 검사에서 판정한다. Y/--yes는 검사 우회가 아니다. 원점·mapping·coverage가 미확인/모순인 Accurate는 처리·최종 확정을 진행하지 않는다. 부족한 coverage를 빈 음성·임의 이동으로 숨기지 않으며 전체 다운로드 fallback·전체 Application 재시도를 추가하지 않는다.

Fast도 사용자 target을 도구/로컬 좌표에 연결하는 기준이 필요하다. Fast 경로가 별도로 검증되지 않았다면 Accurate 실패 후 자동 Fast fallback을 하지 않는다. Fast의 더 약한 결과 조건과 구체 시간축 gate는 Fast 대조 후 확정한다. “Fast이면 시간축을 몰라도 성공”을 채택하지 않는다.

| 상황 | 기존 코드에 연결할 분류안 | 의미 |
|---|---|---|
| 요청 문법/유효 범위 위반 | InvalidInput/InvalidRange, Input | 기존 입력 정책 유지 |
| 사전 확인된 미지원 포맷/조합 | UnsupportedFormat, Input | 포맷 제약이 원인인 경우만 사용 |
| 도구 비정상 종료/timeout/종료 미확인 | ProcessFailed/TimedOut/ProcessTerminationFailed, Execution | 기존 supervisor 결과 유지 |
| 획득 후 필수 시간 정보·mapping·coverage 확인 실패 | VerificationFailed, Execution — 의미 확장 제안 | 원점/입력 검증 실패. 도구 exit 0과 구별 |
| staging frame/sample·stream·끝 경계 검사 실패 | VerificationFailed, Execution | 기존 생성 미디어 검사 의미 |

현재 [OperationErrorCode](../../../src/ClipDownloader.Application/Models/OperationResult.cs)의 VerificationFailed는 설계상 생성 미디어 검사 실패로 기술되어 있다. 입력 시간축 검사까지 포함하는 **문구 확장안**은 통합 시 명시적으로 반영해야 한다. 새 오류 enum·소스 변경을 이번 단위에서 만들지 않는다. 안전한 설명으로 발생 단계와 원점을 확인하지 못함/대응 모순/coverage 부족을 구별하며 임의 browser 내부 오류나 서명 URI를 공개하지 않는다.

## 5. 계약 채택 후 필요한 최소 검증 순서

| gate | 좁은 실험/검증 | 사전 판정 기준 | 현재 |
|---|---|---|---|
| G0 시간 의미 | A/B/C 중 사용자 시간 의미 선택 | 선택 이유·보증/제한을 기록. 기존 일반 정책 유지 | A 사용자 채택 완료 |
| G1 초기 기준·도구 좌표 | 알려진 signed PTS·A/V 서로 다른 시작의 로컬 기준 입력에서 원점 측정과 input seek 비교; CHZZK 초기 fragment 대조와 연결 | 실제 O와 도구의 seek 기준 차이를 정수 time_base로 설명. 초기/중간 probe 기준 혼동 없음 | [G1 제한 대조 완료](PHASE_1_G1_ORIGIN_SEEK_RESULT.md), 범용/원격 측정은 후속 |
| G2 획득·mapping·coverage | G1 원점으로 fractional IN·여유 획득을 변환해 source/local packet/frame 대응과 누락 대조 | 단일 Bᵢ 성립·모순/ambiguous 차단·필요 target coverage. 여유 폭을 기존 2초로 고정하지 않음 | [G2 한 source·두 포맷 대조 완료](PHASE_1_G2_COVERAGE_RESULT.md), 제품 경로 미구현 |
| G3 Accurate video·audio 경계 | 알려진 frame/sample 입력·음성 gap/overlap·AAC priming/padding 대조 | video 선택 PTS 집합 일치. audio 정책/오차를 실험 전에 정하고 유효 presentation 끝 검증 | [G3a 값 차이 진단 완료](PHASE_1_G3A_AUDIO_VALUES_RESULT.md), [G3b 끝/gap 후보 대조 완료](PHASE_1_G3B_AUDIO_ENDS_RESULT.md), [G3c 비정렬/A/V 대조 완료](PHASE_1_G3C_AV_BOUNDARIES_RESULT.md), 새 경계/보정 계약 사용자 채택 완료, G4/G5 대기 |
| G4 Fast·다른 source·분리 A/V | Fast copy의 실제 편차·codec 가능성, YouTube 분리 파일의 공통 시간 관계 | 모드/화질 자동 변경 없음. 원점 오차와 keyframe 편차 구별. 분리 입력 offset 보존 | [G4a Fast baseline 대조 완료](PHASE_1_G4A_FAST_RESULT.md), 공통 A/V 이동량·끝 반례 확인. [G4a-2 제한된 보완 후보 검증](PHASE_1_G4A2_FAST_RESULT.md) 완료. [G4b-1 VP9/로컬 분리 진단](PHASE_1_G4B1_YOUTUBE_LOCAL_RESULT.md) 완료, Opus 24-sample 제거 반례 확인. [G4b-1-2 native 시간/PCM 보존 후보](PHASE_1_G4B12_OPUS_NATIVE_RESULT.md) 제한 검증 완료. [G4b-2a 137/140 개별 원본 범위/coverage](PHASE_1_G4B2_YOUTUBE_RANGES_RESULT.md) 대조 완료. [G4b-2b 공통 presentation/movie 변환](PHASE_1_G4B2B_SHARED_AXIS_RESULT.md) 제한 대조 완료. source descriptor 통합·다른 원격 format·codec/CFR·일반화 미완료 |
| G5 4단계 통합 시작 | G1~G4 결과로 필요한 타입·서비스·오류와 단위별 검증을 확정 | 독립적으로 시작 가능한 단위와 실험 의존 단위를 구별 | 전체 준비 미완료 |

G1의 로컬 3입력·기존 CHZZK 초기 fragment 대조를 마쳤다. G2의 새 원점·실제 획득 mapping/coverage 대조를 한 source·두 포맷에서 마쳤다. G3a 값 차이 진단을 마쳤으며 G3b의 AAC 유효 끝·gap/overlap 후보 대조를 마쳤다. G3c 비정렬 sample/A/V 표시 끝 대조도 마쳤다. 새 경계·보정 계약은 사용자 채택 완료이며 G4/G5는 남아 있다. G1 통과만으로 모든 gate·4단계·Phase 1 완료를 선언하지 않는다. 채택된 6단계는 유지하고 4단계 내 획득/검사/처리의 논리적 커밋으로 이어간다.

## 6. 이번 검토의 실제 결과·커밋 범위

Git 상태·현재 Core/Application 코드·제품 요구사항·Phase 운영 규칙·기존 실험 증거를 읽고 의미/책임을 대조했다. 새 브라우저·원격 호출·미디어 다운로드·합성 영상·패키지·프로젝트·제품 코드 작업은 하지 않았다. 문서 링크·diff 공백·기존 64ms 반례 및 각 후보 target 계산을 검사했다. 문서 검토만이므로 빌드·제품 테스트를 반복하지 않았다.

실험은 후보 O를 적용한 CHZZK trim을 지지하지만, 원점 추출 구현·다른 source·분리 A/V·sample/끝 경계 보증을 확정하지 않는다. 이번 커밋 범위는 이 계약안·설계·baseline 세 문서다. staging/commit은 수행하지 않았다.

## 7. G1 후속 실제 결과

[선택 포맷 원점·seek 대조](PHASE_1_G1_ORIGIN_SEEK_RESULT.md)에서 두 비영점 입력의 기본 상대 seek는 기대한 영상 20 frame·PCM slice와 같았고 AAC priming packet 최소값은 presentation 원점이 아님을 확인했다. copyts/seek_timestamp/accurate_seek 조합의 추가 trim 반례를 기록했다. 원본 presentation 원점 O와 demuxer 시작 F를 구별하며 기본 상대 seek 인자는 Q=O+U−F로 대조한다. AAC의 첫 target PTS는 맞았지만 직접/여유 seek의 PCM 1 LSB 차이를 성공으로 바꾸지 않았다. G1의 제한된 좌표 대조만 완료이며 실제 획득(G2)·audio 값/끝 경계(G3)·범용/분리 입력은 남아 있다. 제품 API/오류 확장안을 이번 실험으로 일괄 채택하지 않는다.

## 8. G2 후속 실제 결과

[G2 실제 획득](PHASE_1_G2_COVERAGE_RESULT.md)에서 fresh initial decoded 시작·remote 입력 시작을 확인하고 Q=O+U−F 구간 인자를 실제 yt-dlp/FFmpeg 명령에 연결했다. 두 포맷 모두 target video 600·audio 470 packet, decoded video PTS 600개와 audio interval/union이 원본과 같았다. 720p audio 10개의 미확인 대응은 모두 target 밖으로 보존했고 필수 packet 제외/잘못된 offset 대조는 조건을 충족하지 못했다. source gap/overlap과 AAC 값/끝 정책(G3), 다른 source·분리 입력/Fast(G4), 계약 통합(G5)은 미완료다. 이번 범위의 G2 대조 완료를 제품 획득 서비스 구현·전체 준비 완료로 간주하지 않는다.

## 9. G3a 후속 실제 결과

[G3a 음성 값 대조](PHASE_1_G3A_AUDIO_VALUES_RESULT.md)에서 G1의 작은 S16 차이는 float 단계부터 존재함을 확인했다. 직접 seek와 full-decode target의 시각/48000 sample 수는 같았지만 값은 달랐으며, 초기 528 samples의 큰 차이와 PNS-enabled noise의 지속 차이를 대조했다. 첫 PNS target 대조는 원인을 구분하지 못했고 noise 보완 대조가 decoder-history 구조를 지지했다. 원본 band/PRNG 직접 관찰과 numeric 허용 정책은 미확정이다. 전체 G3 완료를 선언하지 않으며 다음은 G3b AAC 유효 끝·gap/overlap 처리 후보 검증이다.

## 10. G3b 후속 실제 결과

[G3b AAC 끝·gap/overlap 후보](PHASE_1_G3B_AUDIO_ENDS_RESULT.md)에서 48001-sample 입력의 기본 movie-clock stream/edit 끝과 packet 끝 충돌을 확인했고 precise clock에서는 일치했다. G3a noise의 raw tail 512는 해당 파일의 확인된 유효 끝 밖임을 구별했다. timestamp 유지 AAC는 source 불연속 개수는 유지하지만 위치를 옮겨 정확한 union 보존에 실패했다. source 빈 시각을 0으로 채우고 중복에서 앞선 값을 유지하는 sample-grid 후보는 합성·CHZZK 두 입력의 lossless 모델과 정확히 같았다. 이 후보의 제품 채택·비정렬 boundary·A/V 마지막 표시 끝은 미확정이며 다음은 G3c 하위 계약/경계 대조다. 알려지지 않은 획득 누락을 보정으로 숨기지 않고 기존 mapping/coverage gate는 유지한다.

## 11. G3c 후속 실제 결과·처리 규칙 선택 대기

[G3c 비정렬 경계·A/V 출력](PHASE_1_G3C_AV_BOUNDARIES_RESULT.md)에서 atrim의 native sample-rate 단위, 최근접 boundary의 범위 밖 sample, AAC phase 변환의 1-sample residue, 마지막 video frame 표시 overhang을 분리했다. 48kHz·44.1kHz ceil native 범위의 source 값과 합성 30/CHZZK 600 video PTS 집합은 정확히 같았다. output t로도 마지막 frame 표시 끝은 잘리지 않았다. 결과 제5절에 source gap의 zero fill/중복 first-wins·audio ceil 경계와 명시적 render 양자화·video PTS membership/실제 표시 끝·metadata tail 검사 묶음을 권고했지만 새 처리 의미는 미채택이다. 기존 사용자 시간 의미와 일반 정책은 유지한다. 채택 시 이 정의로 계약을 통합하고 G4 Fast/다른 source·분리 A/V와 G5를 진행한다. 엄격한 표시 OUT 대안은 현재 경로 미충족이므로 추가 경로 검증 또는 실행 거절이 필요하다.

## 12. G3c 처리 규칙 채택

2026-10-11 사용자가 [G3c 결과 제5절](PHASE_1_G3C_AV_BOUNDARIES_RESULT.md)의 권고 묶음을 채택했다. 확인된 source gap zero fill·중복 first-wins, audio IN/OUT native sample ceil 및 계산한 boundary 기준의 명시적 render 양자화, video source PTS [IN,OUT) 선택과 실제 표시 끝 안내, 확인된 metadata의 유효 끝 검사를 적용한다. 실제 첫 stream STARTPTS로 지연을 독립 제거하지 않고 미확인 mapping/획득 누락을 보정으로 숨기지 않는다. G3 정책 채택과 아직 없는 제품 구현·미검증 입력 지원을 구분한다. 다음은 G4 Fast/YouTube 분리 A/V·codec/CFR 검증이며 G5의 구체 타입/오류 통합도 남아 있다.

## 13. G4a Fast baseline 후속

2026-10-11 [G4a Fast copy 대조](PHASE_1_G4A_FAST_RESULT.md)에서 합성 GOP/B-frame·7초 timestamp 이동군·기존 CHZZK prefix의 원본 packet/frame 대응을 확인했다. 비정렬 합성은 4352/240000초, CHZZK 단순 copy는 130/48000초의 video/audio 이동량 차이를 보였고 avoid_negative_ts disabled로도 바뀌지 않았다. keyframe 정렬 대조군의 공통 이동량만 일치했으며 decoded video 끝과 선언 끝은 별도 반례였다. 실행 성공·keyframe 편차·source 관계·전체 staging 검사를 구별한다. 다음 G4a-2는 공통 source 축·keyframe copy 후보와 MP4 끝 의미 대조다. YouTube 분리 A/V·VP9·codec/CFR·G5·전체 준비는 남아 있다.

## 14. G4a-2 공통 시간축·MP4 presentation 끝 후속

2026-10-11 [G4a-2 보완 대조](PHASE_1_G4A2_FAST_RESULT.md)에서 확인된 선행 keyframe K·Bᵢ 적용 packet 이동·입력 timestamp 축의 종료 인자를 연결했다. 종료 좌표 오류의 empty MP4/decode 실패를 보존했고, 보완 후보 6개는 공통 A/V 이동량·필수 target coverage·sample table/edit/decoded 끝을 통과했다. 대표 출력 decoded video hash도 source와 같았고 초기 audio 지연을 유지했다. ffprobe duration_ts만으로 표시 끝을 판정하지 않고 stts/ctts/elst 의미를 적용한다. 기본 CTS 후보가 통과하며 noedit 대조는 관계를 훼손했다. 제한된 native audio grid K·H.264/AAC 결과이며 제품 구현·전체 Fast 지원을 뜻하지 않는다. 다음은 G4b YouTube 분리 관계/VP9와 codec/CFR·G5다.

## 15. G4b-1 YouTube 로컬 진단 후속

2026-10-11 [G4b-1](PHASE_1_G4B1_YOUTUBE_LOCAL_RESULT.md)은 기존 획득 파일만 사용했다. 늦게 나타나는 VP9의 입력 분석 시간 확대로 profile/pix_fmt·vpcC를 확보해 header/probe/decode 실패를 보완했다. byte 확대만으로는 실패가 유지됐다. local R의 분리/재병합 packet 관계는 확인했으나 원격 S와의 관계는 미검증이다. MP4 Opus는 packet PTS/payload가 같아도 skip 312→336으로 첫 decoded 24 samples를 제거했고 PCM suffix 비교로 확인했다. 허용 오차·고정 sample 보정을 채택하지 않는다. 다음은 G4b-1-2 native Opus 경계 대조 후 G4b-2 실제 원격 분리 관계/coverage다. 일반화·codec/CFR·G5·전체 준비는 남아 있다.

## 16. G4b-1-2 native Opus 시간·PCM 보존 후속

2026-10-12 [native Opus 대조](PHASE_1_G4B12_OPUS_NATIVE_RESULT.md)에서 EBML block timestamp/CodecDelay 6.5ms·pre-skip 312를 확인하고 demux 7ms 차감과의 24-sample 차이를 모든 packet에서 대조했다. 계산 잔여 적용 원본/분리 후보는 native PTS·전체 PCM·dOps/edit 끝·영상 값을 보존했다. 8ms/384 통제는 계산 잔여 0으로 고정 +24 일반화를 반박했고 0/틀린 잔여 대조는 거절했다. native R과 coarse demux R을 구별하며 원격 S 관계를 미검증으로 유지한다. 다음 G4b-2 원격 분리 관계/coverage·codec/CFR·G5가 남아 있다. 제품 코드·새 설치/원격 획득·전체 준비/Phase 완료를 선언하지 않았다.

## 17. G4b-2a 실제 원본 초기·indexed 범위 후속

2026-10-12 [YouTube 137/140 원본 대조](PHASE_1_G4B2_YOUTUBE_RANGES_RESULT.md)에서 metadata 조회·각 native 초기 presentation 0·SIDX earliest 0을 확인했다. 첫 FFprobe timeout과 범위 오류를 보존하고, bounded range-query-only 획득에서 source-local offset 0·video target 300 PTS·audio target 441000 native samples와 coverage를 확인했다. 두 file의 0이 같다는 사실만으로 공통 source 축을 확정하지 않는다. 다음 G4b-2b는 provider manifest/통합 A/V 참조의 공통 관계를 보완한다. 다른 format·codec/CFR·G5·전체 준비/Phase 완료는 남아 있다.

## 18. G4b-2b provider group·content·movie 변환 후속

2026-10-12 [공통 HLS presentation 대조](PHASE_1_G4B2B_SHARED_AXIS_RESULT.md)에서 master의 270→audio group 234 연결을 확인하고 선택 DASH137/140과 coded content/native timestamp를 대응했다. 영상 900개 값/PTS는 movie edit512/15360을 고려한 참조와 일치했고 AAC1293 frames/ID3 5개는 원본44.1kHz sample 진행과 floor90kHz projection에 대응했다. raw media와 selected movie 축을 구별하며 이미 edit 적용된 DASH에 1/30을 다시 더하지 않는다. 기존 A와 movie presentation 의미를 유지하고 단순한 두 시작값0만으로 판정하지 않는다. 제한된 관계 대조를 완료했으며 G4c와 G5 descriptor/API 통합·다른 pair 지원·전체 준비는 남아 있다.


## 19. G4c-1 cadence와 movie 표현 한계

2026-10-12 [CFR/VFR 대조](PHASE_1_G4C1_CADENCE_RESULT.md)에서 실제 VFR 입력의 r/avg30 일치 반례와 기본 movie1000의 sub-ms PTS 이동을 확인했다. native 유리수 PTS lattice·검사 창을 기록하고 declared FPS와 실제 frame 번호 보장을 구별한다. movie clock90000의 제한 후보 4개는 selected PTS를 모두 보존했으나 고정90000·전체 VFR Accurate 지원을 채택한 것은 아니다. G3c membership/공통 shift/표시 끝 계약은 유지한다. CHZZK1080의 upstream1ms 양자화는 가설이며 native clock의 허용 오차로 승격하지 않는다. 다음은 G4c-2 codec 경계와 G5 descriptor/최종 성공·거절 계약 통합이다. 전체 준비/제품 구현 완료는 아니다.


## 20. G4c-2 codec capability와 sample 보존 경계

2026-10-12 [codec 경계 대조](PHASE_1_G4C2_CODEC_BOUNDARIES_RESULT.md)에서 local3종 MP4 copy/encode capability와 payload/picture 보존을 확인했으나 native/coarse clock·skip/padding 차이로 단순copy의 audio 보존을 거절했다. AAC rawPCM 앞16개·Opus 시작24개 손실과 rawtail648개 차이를 대조했다. rawPCM 총수와 movie 유효끝을 혼동하지 않고 고정보정·전체codec 지원을 선언하지 않는다. Fast/Accurate의 필수 source/native·초기화·end gate와 기존 오류 코드 연결 후보를 G5a에 전달한다. 일반 정책과 G3c 계약은 유지하고 descriptor/API 확정·제품 구현·전체 준비는 아직 남아 있다.


## 21. G5a 최소 descriptor·gate 통합안

2026-10-12 [G5a 통합안](PHASE_1_G5A_DESCRIPTOR_GATE_DRAFT.md)은 origin·shared axis·native/local relation·필요 coverage·출력 expectation을 필수 항목별 상태/검사 범위로 분리했다. 사이트/source identity는 Application, local/native 시간과 render 계획은 Media.Core에 둔다. native 보정 후 B를 다시 계산해 이중 보정을 피하고 G3c audio ceil/render residual·video membership·Fast 공통K를 연결한다. 기존 오류 enum을 유지하며 요청 해상도 미제공 Input과 media 전략/검증 실패 Execution을 구별한다. G2/G3c 기존 좌표와 signed ceil·double 범위·오류명 문서 검산만 완료했다. CLR/API·지원 전략·FPS 검사·decoder 준비/빈 target 및 시작 조건은 G5b에 남았고 제품 구현·전체 준비는 아니다.


## 22. G5b API·초기 전략·단위별 준비

2026-10-12 [G5b API/readiness](PHASE_1_G5B_API_READINESS.md)에서 기존 시간모델을 유지한 signed/native/range API와 이후inspection/acquisition/probe/processing 경계를 정리했다. FPS 미확인은 사용자 확인후 source inspection으로 조사해 ClipJob 생성의 순환 의존을 피한다. empty media 집합은 초기전략 미지원으로 거절하고 audio partial support는 requested ceil render 기준의 지연/끝을 보존한다. 이 arithmetic 검토를 새 codec출력 성공으로 승격하지 않는다. 4a-1 기본형은 준비 완료지만 Accurate AAC decoder preparation·HTTP/source adapter·전체지원/준비는 미완료다. 다음 제품 구현은4a-1의3타입과 의미 테스트로 한정하고 논리적커밋마다 보고한다. 기존G3c/일반정책 재승인·제품코드/설치/미디어 작업은 이번에 하지 않았다.


## 23. 4a-1 signed/native 기본형 구현

2026-10-12 [4a-1 결과](PHASE_1_STAGE_4A1_RESULT.md)의 exact signed time/native PTS/half-open range를 구현하고 Core121개 및 솔루션빌드를 검증했다. 사용자시간타입을 음수local좌표에 재사용하지 않고 exact bridge·native rounding을 별도로 둔다. 이 수학/API 검증을 source mapping·coverage·codec preparation 성공으로 승격하지 않는다. 다음은4a-2 결과/관찰/획득 계약이며 치지직source adapter·처리 및 전체 준비/Phase완료는 아직 남아 있다.


## 24. 4a-2a scoped 관찰/native 기본형

2026-10-12 [4a-2a](PHASE_1_STAGE_4A2A_RESULT.md)의 공용결과·관찰 scope/fact·native relation/audio lattice를 구현하고 Core136개·솔루션빌드를 검증했다. snapshot/stream/검사 종류/범위와 demux/native 기준을 구별하고 partial·unknown·contradicted를 verified 범위와 혼합하지 않는다. constant correction 후 source B 재계산·phase를 보존한 sample-grid 산술을 검사했으나 실제source/native 증명·coverage·decoder 준비를 생성한 것은 아니다. 다음4a-2b는 Applicationinspection/acquisition 계약이며 4a-2전체·치지직adapter·미디어 처리/전체준비는 아직 남아 있다.


## 25. 4a-2b selected source/획득 계약

2026-10-12 [4a-2b](PHASE_1_STAGE_4A2B_RESULT.md)는 공용 scope/fact를 선택 snapshot·bounded inspection·source 원점/축/끝·acquisition 요청/결과에 연결했다. FPS 미확인 검사는 ClipJob/시간 범위 없이 요청하고, 확인된 rate와 같은 선택/workspace일 때 target 요청을 구성한다. 파일의 포맷 provenance·inspection/acquisition ID·native 기준·필요 frame/sample coverage와 offset scope를 구별한다. Unknown/Contradicted와 실제 확정을 혼합하지 않고 구조 검사를 source 사실 검증으로 승격하지 않는다. Downloader 307개와 솔루션 빌드를 검증했으며 다음 4a-3 adapter·coverage/native 증거 생산·최종 처리 gate는 남아 있다.


## 26. 4a-3a bounded HTTP 기반

2026-10-12 [4a-3a](PHASE_1_STAGE_4A3A_HTTP_RESULT.md)는 Infrastructure 내부 byte reader의 exact header/query 범위·bounded EOF·body 완전성·취소·safe failure를 구현했다. HTTP 자동 retry/fallback은 없고 기존 yt-dlp 정책과 별도로 scoped transport 정책을 명시했다. fake HTTP/stream 327개 테스트 및 솔루션 빌드를 검증했으나 실제 source bytes/native 관계·coverage/decoder 준비를 확인한 것은 아니다. 다음 private locator/headers와 초기/target source 해석·inspection/acquisition adapter에서 증거를 생산해야 한다. 전체 준비/Phase 완료는 아직 미완료다.


## 27. 4a-3b private locator/metadata 경계

2026-10-12 [4a-3b](PHASE_1_STAGE_4A3B_LOCATOR_RESULT.md)는 기존 metadata 실행 정책을 내부 query로 재사용하고 같은 응답에서 선택 source/format과 raw locator/headers를 검증한다. public VideoInfo/snapshot에 signed URL을 추가하지 않으며 주소 갱신 자체를 asset generation/native 관계의 증명으로 쓰지 않는다. 초기 Unknown FPS 해소와 알려진 선택/FPS 변경을 구별했고 fake query/HTTP 연결 회귀354개·솔루션빌드를 검증했다. 다음 CHZZK HLS 초기/target 참조·native proof·실제 inspection/acquisition adapter 및 전체 준비는 미완료다.


## 28. 4a-3c-1 HLS playlist 참조 계획

2026-10-13 [4a-3c-1](PHASE_1_STAGE_4A3C1_HLS_RESULT.md)은 selected leaf 조회·제한된 media playlist 해석·exact EXTINF 참조/budget 계획을 구현했다. playlist 좌표는 source/native/user와 별개이며 선택 segment/sequence/date 자체가 mapping/coverage 증명은 아니다. synthetic/fake HTTP의 Downloader373개와 솔루션빌드를 검증했으나 원본fragment/native/파일snapshot·실제source adapter는 아직 없다. 다음 init/segment 참조수집·native proof에서 identity·origin·target 관계를 확인해야 하며 전체 준비/Phase완료는 미완료다.


## 29. 4a-3c-2 bounded 참조와 파일 수명

2026-10-13 [4a-3c-2](PHASE_1_STAGE_4A3C2_COLLECTION_RESULT.md)는 같은 playlist/leaf plan의 complete init/segment를 누적 budget·소유 파일·취소/cleanup 조건으로 수집한다. bytes/SHA/playlist범위/provenance는 반환하지만 source/native/decoder/target 관계를 증명한 것은 아니다. cancellation/close 실패 교차를 수정하고 fake HTTP/실제 fixture 파일의385개 테스트·솔루션빌드를 검증했다. 다음 초기 source참조의 native 원점/FPS 관찰과 public inspector/잔존자원 연결이 필요하며 실제 CHZZK/전체 준비는 미완료다.

## 30. 4a-3c-3a 초기 참조 Demux 관찰

2026-10-13 [4a-3c-3a](PHASE_1_STAGE_4A3C3A_OBSERVATIONS_RESULT.md)는 bounded JSON의 exact stream/frame PTS와 선언/평균 FPS를 internal read-only 관찰로 보존한다. 누락은 대체하지 않으며 basis는 Demux다. start_pts/최초 frame/동일 FPS 자체가 source O/native 관계/CFR/coverage 증명이 아니다. synthetic JSON의 Downloader404개·솔루션빌드를 검증했다. 다음 local FFprobe 실행·수집파일 hash identity, codec/container 보정/native proof와 inspector 연결이 필요하다. 실제 source/전체 준비는 미완료다.

## 31. 4a-3c-3b local probe와 byte identity

2026-10-13 [4a-3c-3b](PHASE_1_STAGE_4A3C3B_PROBE_RESULT.md)는 collector reference의 local bytes를 read share lock·실행 전후 길이/SHA로 연결하고 version/local FFprobe·종료/출력/취소 제한을 적용한다. success도 Demux 관찰이며 remote identity/native 원점/CFR/coverage 증명이 아니다. fake runner/실제 tiny파일의 Downloader416개·빌드와 기존 G2파일의 별도 실제 CLI 옵션을 검증했다. real adapter end-to-end·codec/container/native 증거·inspector·획득 연결은 남았다. 파일 cleanup은 collector/caller 수명에 속하며 probe는 소유 파일을 삭제하지 않는다.

## 32. 4a-3c-3c-1 MP4 timing header 경계

2026-10-13 [4a-3c-3c-1](PHASE_1_STAGE_4A3C3C1_CONTAINER_RESULT.md)는 movie/track timescale·explicit track ID·edit 존재/entry·signed rate를 bounded standalone stream reader로 보존한다. track ID를 stream index+1로 대체하지 않는다. edit 없음/empty edit/header 읽기 성공 자체가 native 변환0·AAC priming·source O/지원/coverage 증명이 아니다. 아직 collector/probe hash 수명에 결합하지 않았고 codec/fragment를 검사하지 않는다. malformed box 예외 경계를 수정한 synthetic432개·솔루션빌드를 확인했다. 다음 byte identity/track 연결과 native 증거 gate가 남았다.

## 33. 4a-3c-3c-2 동일 bytes의 explicit track 연결

2026-10-13 [4a-3c-3c-2](PHASE_1_STAGE_4A3C3C2_LINK_RESULT.md)는 collector reference의 lock/pre·posthash 수명에 container 읽기를 결합하고 explicit FFprobe id·동일 exact clock을 대조한다. index+1/list order 추정은 없으며 미확인/불일치에는 연결을 발행하지 않는다. 성공도 Demux/container 관찰이며 handler/codec/fragment/native 원점/coverage 증명이 아니다. 최종452개·빌드와 기존 초기 참조 복사본의 실제 ToolLocator/ProcessRunner/FFprobe 연결213frames를 확인했다. actual selected service/HTTP collector·native evidence는 남았다. sandbox ProbeExecution I/O를 clock 모순으로 처리하지 않고 안전한 실패 단계/분류를 보완했다.

## 34. 4a-3c-3c-3a fragment decode 관찰

2026-10-13 [4a-3c-3c-3a](PHASE_1_STAGE_4A3C3C3A_FRAGMENT_RESULT.md)는 동일 bytes의 bounded moof offset·tfhd TrackId/flags·optional unsigned32/64 tfdt를 보존한다. missing tfdt는 null이며 repeated/nonmonotonic anchors를 자동 보정하지 않는다. trun/default/codec/skip/priming/sample presentation은 아직 미확인이다. tfdt를 PTS/O나 native correction0으로 승격하지 않는다. synthetic467개·빌드와 보존된 실제 local fixture adapter의4anchors를 검증했다. 다음 codec/sample/native evidence·inspector/획득 연결이 남았다.

## 35. 4a-3c-3c-3b-1 codec/decoded audio 관찰

2026-10-13 [4a-3c-3c-3b-1](PHASE_1_STAGE_4A3C3C3B1_CODEC_RESULT.md)는 같은 파일의 FFprobe codec labels·sample parameters·extradata_size와 decoded frame audio parameters를 별도로 보존한다. 누락을 codec default/time_base/다른 관찰로 채우지 않는다. extradata 크기와 profile 관찰은 codec 설정 bytes·native/priming/지원 proof가 아니다. synthetic487개·빌드와 보존된 실제fixture의 codec/213frames를 확인했다. packet skip/padding·container sample presentation/native gate·inspector/획득은 남았다.

## 36. 4a-3c-3c-3b-2 packet timing/skip 관찰

[4a-3c-3c-3b-2](PHASE_1_STAGE_4A3C3C3B2_PACKET_RESULT.md)는 같은 파일의 packet/frame 결합 출력에서 signed PTS/DTS와 explicit skip/padding/reasons를 보존한다. missing clocks/side data를0으로 추정하지 않으며 unknown side는미해석으로 남긴다. 최종507개·빌드와local CHZZK/AAC edit oracle를 실제대조했다. 원시 음수 priming packet은O가 아니다. native/sample/codec gate·source inspector/획득 연결과전체 검증은남았다.

## 37. 4a-3c-4 HLS initial observation pipeline

[4a-3c-4](PHASE_1_STAGE_4A3C4_INITIAL_PIPELINE_RESULT.md)는 재검증된 resolved locator의동일선택과budget을first whole reference/collector/probe에 연결한다. 성공은internal observation 후보이며native/원점/FPS fact가 아니다. 실패/취소에도소유파일을RemainingFiles로 인계하고uncertain termination을취소/삭제로숨기지 않는다. public adapter/workspace cleanup인계는아직없다. 최종synthetic520개·빌드를확인했고actual pipeline site/media 실행은미완료다.

## 38. 4a-3c-5 bounded native sample gate

[4a-3c-5](PHASE_1_STAGE_4A3C5_NATIVE_RESULT.md)는 최초 AVC/AAC-LC fMP4 전략의실제sample duration/CTS/defaults/bytes와packet/frame을대조한다. 성공은해당원본참조의timing 관계이며first source provenance와public O/FPS/axis 발행은caller 책임이다. decoder/history/PCM/CFR/target/전체끝을추정하지 않는다. edit/비zero skip/복합codec은첫 gate에서미지원이다. 최종546개·빌드와보존된CHZZK native sample/AAC edit실제대조를검증했다. public adapter/소유실패 인계와획득은남았다.

## 39. 4a-3c-6 public inspection와resource 인계

[4a-3c-6](PHASE_1_STAGE_4A3C6_INSPECTION_RESULT.md)는 same scoped instance의metadata/first HLS/native proof에서first selected O·scoped FPS·single mux axis를발행한다. whole native end/CFR/decoder history는추정하지 않는다. ISourceResourceAccess는실패/취소의owned files/cleanup/termination 상태를public OperationResult.Data와독립적으로인계한다. 최종555개·빌드와실제CHZZK first reference를검증했다. target/YouTube adapter·소유workspace cleanup/host 연결과전체4a 검증은남았다.

## 40. 4a-3c-7 HLS target acquisition

[4a-3c-7](PHASE_1_STAGE_4A3C7_ACQUIRE_RESULT.md)는confirmed O를사용한참조후보를수집하고실제native cfg/clock/필요sample support를대조한scoped B0 mapping을반환한다. first hash재확인과누적budget/소유registry를유지하며failure/cancel Data=null에서도files를인계한다. PCM/decoder history/CFR/최종output proof와다르다. 최종563개·빌드와실제CHZZK10~12초bounded acquire를검증했다. YouTube indexed/분리 관계·양사이트통합과전체4a는남았다.

## 41. 4a-3d-1 init/SIDX byte references

[4a-3d-1](PHASE_1_STAGE_4A3D1_SIDX_RESULT.md)는independently confirmed resource length와bounded prefix에서init/direct byte references 및exact index time을읽는다. unsigned time/SAP는관찰이며user/movie/native O/keyframe/decoder 준비를추정하지 않는다. 최종574개·빌드와actual137/140 prefix각65536bytes를검증했다. actualindex/header수집/소유파일과native/shared-axis 연결·전체4a는남았다.

## 42. 4a-3d-2 indexed byte collection

[4a-3d-2](PHASE_1_STAGE_4A3D2_COLLECTION_RESULT.md)는 same-prefix locator/index 계약 아래 whole direct references를 정확한 byte 범위로 소유 파일에 수집한다. 요청/실제 index 구간과 byte hash를 보존하며 movie/native O 또는 coverage를 추정하지 않는다. Downloader581개·빌드 경고/오류0. prefix 통합/public 연결·native/edit/shared axis와전체4a 검증은남았다.
