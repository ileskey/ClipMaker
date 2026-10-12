# Phase 1 G5a 최소 descriptor·실행 gate·오류 통합안

작성일: 2026-10-12 (Asia/Seoul). 시작 HEAD `f48aa91062cdc82fe98bc3f8a377365c6e09d9b7`, branch `phase/1`, clean. Phase Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **G5a 책임·최소 필드·gate 통합안 작성/문서 검산 완료**. G5b CLR/API·지원 전략·구현 커밋 순서 정리와 시작 조건 점검은 남아 있다. 이 문서를 제품 타입 구현·전체 설계 확정·전체 준비 완료로 표시하지 않는다. [기존 계약 통합](PHASE_1_CONTRACT_INTEGRATION.md), [source 시간 계약](PHASE_1_SOURCE_TIMELINE_CONTRACT_DRAFT.md), [일반 설계](PHASE_1_DESIGN.md), [검산 기록](evidence/PHASE_1_G5A_CONTRACT_REVIEW.json)을 연결한다. 기존 일반 정책·선택 포맷 시간 의미 A·G3c 처리 의미는 재승인하지 않는다.

## 1. 현재 소스와 연결할 지점

현재 Media.Core에는 FrameRate·TimecodeInput·Timecode·MediaTimeRange가 있고 signed timestamp·probe/processing 모델은 없다. Application에는 VideoInfo/FormatSelection/OperationResult와 조회/진단 계약이 있지만 AcquiredMedia·PreparedClip·MediaInput·획득/처리 서비스는 없다. 기존 코드의 FormatSelection 성공은 metadata 후보 선택이며 실행 가능성·원본 음성·mode 호환성 증명이 아니다.

Timecode는 내부 BigInteger로 exact seconds를 보존하지만 그 분자/분모를 공개하지 않는다. 후속 연결은 `wholeSeconds + FF*FPS.Denominator/FPS.Numerator`를 검증된 Input/FrameRate에서 계산한다. TimeSpan으로 변환했다가 signed media 시간으로 되돌려 정확도를 잃지 않는다. 기존 Timecode의 nonnegative 입력/TimeSpan 경계·짝수 반올림·MediaTimeRange 동작은 유지한다. 별도 signed rational 타입이 필요하며 기존 public 모델을 광범위하게 재작성하지 않는다.

OperationResult 실패/취소에서는 Data가 null이다. 실패에 raw probe/evidence를 끼워 넣거나 generic 결과 규칙을 바꾸지 않는다. 측정 결과·검사 제한은 acquisition/probe가 성공적으로 측정한 snapshot에 담고, qualification 실패는 기존 Error와 안전한 내부 진단으로 설명한다. `AcquireAsync`의 성공은 파일 획득 결과이지 clip 성공이 아니다.

## 2. 시간 표현과 identity 불변식

- **SignedMediaTime**(가칭, Media.Core): 초 단위의 기약 signed rational, BigInteger 분자/양수 분모, 정확한 비교·덧셈/뺄셈·native grid floor/ceil. zero는0/1. NaN/∞·분모0·negative denominator 상태는 허용하지 않는다. NativeTimestamp는 signed Int64 PTS와 양수 rational time_base를 유지한다. 정수 PTS의 부재를0으로 바꾸지 않는다.
- **PresentationRange**(가칭, Media.Core): signed `[Start,End)`, End>Start. MediaTimeRange의 사용자 nonnegative/FPS 문법과 분리한다. 검사 구간·coverage 구간·출력 표시 범위를 같은 하나의 Duration으로 합치지 않는다.
- **SelectionSnapshot**(Application 의미): normalized source reference·선택 video/audio IDs·요청 mode/quality·선택 metadata snapshot. source identity/포맷/선언 FPS가 바뀌면 기존 좌표를 무효화하며 조용히 재선택하지 않는다. metadata FPS가 처음 Unknown이었다가 확인되는 경우는 기존 재검사 정책의 해소로 구별한다.
- **AcquisitionSnapshotId**와 opaque asset generation(Application 의미): 작업의 새 획득/검사 결과를 이전 결과와 혼합하지 않는 로컬 식별자. 서비스가 원본 version을 제공하지 않으면 “서버 generation 확인”을 주장하지 않는다. 동일 URL/format ID만으로 영구적인 content identity를 보장하지 않는다.
- 측정 fingerprint/reference와 asset identity는 내부 증거용이며 인증·signed URL을 public descriptor에 담지 않는다. 시간이 다를 수 있는 여러 선택 포맷에 고정 O/B를 공유하지 않는다.

exact model이 값을 표현해도 FFmpeg의 seconds parser·double 식·mux clock이 같은 값을 표현한다는 뜻은 아니다. 처리 binder가 확인된 clock·정수 범위·encoder/container 표현을 별도 검사한다. 예컨대 setts double 식의 피연산자/중간 합이 exact integer 범위를 벗어나면 이 전략으로 성공을 주장하지 않는다. 새로운 큰 정수/timestamp 엔진·무제한 dump·generic EBML/MP4 수정기를 선행 구현하지 않는다.

## 3. 최소 descriptor와 계층 소유권

이름은 후속 파일 배치를 위한 후보이며 이번 문서로 CLR 생성자·enum 이름·시그니처가 최종 확정된 것은 아니다. nullable 하나로 unknown과 contradictory를 합치지 않고 **필수 항목별** Verified/Unknown/Contradicted 및 안전한 이유를 구별한다. 전역 `IsVerified=true`만으로 모든 gate를 우회하지 않는다.

| 묶음 | 최소 의미/필드 | 소유 계층 |
|---|---|---|
| SourceTimelineDescriptor | snapshot/source selection, 선택 movie/presentation 축의 Origin O와 그 의미·초기 검사 근거, metadata duration와 media 유효 끝의 별도 상태, 분리 A/V의 공통 축 관계/검사 범위 | Downloader.Application |
| AcquiredMedia | acquisition snapshot, 소유한 로컬 파일 목록, 선택 stream 대응, source descriptor, stream별 S=L+B relation와 확인 범위, 필요한 coverage의 상태·제한 | Downloader.Application |
| LocalMediaFile/StreamReference | workspace 소유 파일의 절대경로·파일/stream index·video/audio kind, 현재 검사 snapshot 연결 | Media.Core |
| StreamInspection | 관찰 PTS 필드/PTS 누락, local time_base, codec/profile/pix_fmt 또는 sample_rate/channels, 실제 관찰 구간/완전성, nullable stream 시작/끝과 해석 | Media.Core |
| NativePresentationModel | 관찰 demux PTS와 native presentation의 확인된 관계·범위, codec/container skip/pre-skip/유효 padding·edit 해석, audio lattice origin Gₐ/rate와 valid support, video PTS/표시 duration의 출처·완전성 | Media.Core |
| FrameRateInspection | nullable rational rate·선택/검사 출처, declared r/avg와 inspected cadence를 구별, 관찰 창·CFR-compatible/VFR/Unknown/Contradicted와 근거 | Media.Core |
| QualifiedMediaInput | 필수 local/native facts를 갖춘 파일/stream 참조. 원본 URL·platform·format IDs·O라는 사이트 의미 없이 처리에 필요한 native 좌표만 전달 | Media.Core, Application에서 구성 |
| StreamProcessingPlan | 검증한 local/native 추출 구간, 공통 render 기준을 반영한 stream별 translation, audio native indices/render boundary·명시 quantization, Fast의 검증된 K, 실행 가능한 codec/container 전략 및 기대 결과 | Media.Core |
| OutputExpectation/VerificationReport | 기대 video membership/시각/표시 끝, audio 유효 indices/시각/끝/관찰된 보정 수·metadata, 실제값·검사 창/제한·차단 사유. result staging 정보와 최종 확정 상태는 별개 | 공유 의미는 Media.Core, source 비교·최종 판정은 Application |

NativePresentationModel의 관계는 **관찰값과 실제 native 의미가 확인된 범위**만 표현한다. Opus처럼 C=24samples 잔여를 확인한 특정 입력에만 그 변환을 담으며 모든 파일에 +24를 적용하지 않는다. local demux/native 관계가 변화하면 constant offset으로 덮지 않고 첫 전략 범위 밖으로 거절한다. B는 채택한 **native local L** 기준으로 측정/확인한다. 기존 demux 기반 B에 native correction을 추가했다면 B도 다시 계산해야 하며 이중 보정하지 않는다.

stream absence는 확인된 없음만 허용한다. presence Unknown·획득 중 누락은 무음 원본으로 승격하지 않는다. source audio의 실제 시작 지연/유효 끝과 그 내부의 확인된 gap을 구별한다. 알려진 source 내부 gap/overlap만 G3c zero-fill/first-wins로 재구성하며 decoder padding·원래 audio 시작 전/끝 후·획득 누락을 동일한 gap으로 분류하지 않는다.

public 모델에 전체 packet/hash·raw JSON·도구 명령/주소를 저장하지 않는다. probe 결과는 상한이 있는 관찰 목록/요약과 범위·필수 누락을 보존한다. bounded packet/frame 자료와 정확한 대응 검산은 Infrastructure 내부에서 수행하며 Application에는 판정에 필요한 최소 값·검사 범위·안전한 이유만 전달한다. 무조건 “두 anchor면 검증됨” 규칙은 없다. 모순·필요 구간의 ambiguous hash·미검사 범위를 숨기지 않는다.

## 4. 좌표와 처리 요청의 구성

동일 snapshot의 확인된 공통 source presentation에서 `S=U+O`, 각 stream에서 `S=Lᵢ+Bᵢ`다. target `[S_IN,S_OUT)=[O+U_IN,O+U_OUT)`를 정하고 각 local 범위를 이 식으로 변환한다. seek용 demux 시작 F는 O와 구별하며 확인된 상대 seek 후보는 `Q=O+U−F`다. seek 인자의 실제 표현은 acquisition 전략에 속하고 Q를 실제 파일의 원점/coverage 증명으로 쓰지 않는다.

**Accurate video**는 source PTS `[S_IN,S_OUT)` 집합을 선택하고 `Rᵥ=S−S_IN`으로 옮긴다. 마지막 frame의 확인된 duration에서 실제 표시 끝을 계산해 전달한다. DTS·summary FPS 또는 `-t`로 membership/끝을 대체하지 않는다. VFR 미확인만으로 임의30fps 변환이나 실제 frame 번호 보장을 추가하지 않는다.

**Accurate audio**는 확인된 lattice `S(n)=Gₐ+n/R`에서 `n_IN=ceil((S_IN−Gₐ)R)`, `n_OUT=ceil((S_OUT−Gₐ)R)`를 구한다. audio render 기준 `Qₐ=Gₐ+n_IN/R`, explicit residual `δₐ=Qₐ−S_IN`을 기록하고 `Rₐ=S−Qₐ`를 기대한다. 실제 first available sample을0으로 만드는 것이 아니다. `0<=δₐ<1/R`은 계산한 이 boundary의 성질이며 sample 손실/metadata 틀림을 허용하는 포괄 tolerance가 아니다. S/G가 negative여도 exact ceil을 적용한다. FFmpeg atrim 인자의 native sample-rate 단위를 common tick 숫자로 바꾸지 않는다.

valid audio support 밖의 원래 지연/끝을 보존하고 내부의 확인된 source 불연속만 보정한다. 요청이 actual audio support와 전혀 겹치지 않는 경우의 결과 표현·빈 frame/sample 집합 처리·압축 codec의 decoder-history 준비 범위는 G5b에서 구체화한다. 이번 안으로 임의 silence·전체 decode/download를 승인하지 않는다. 미정 사례는 보존 성공으로 반환할 수 없다.

**Fast**는 확인한 선행 keyframe K·target coverage와 packet/native sample 의미를 사용하고 양 stream 모두 `R=S−K` 기준이다. Core에 주는 translation은 `Bᵢ−K`이며 stream별 첫 PTS를 각각 제거하지 않는다. 실제 시작과 video/audio별 표시 끝을 보고하고 요청 경계와의 차이를 설명한다. K의 native sample 표현·keyframe 의미·출력 header/edit/skip/end가 확인되지 않은 전략은 지원 경로로 승격하지 않는다.

source와 output 비교는 Application의 책임이다. 공유 처리 서비스는 전달된 native range/translation/expectation으로 staging을 만들고 실제 처리/제한을 반환한다. 사이트 source 의미를 공유 Core로 누출하지 않는다. `Processed`/파일 존재/exit0은 `Verified`/최종 성공과 다르다.

## 5. 진행 gate와 차단 시점

| 시점 | 통과 조건 | 실패·미확인 시 |
|---|---|---|
| 조회/선택·Prepare | 기존 URL/완료영상/quality·문법·알려진 구간/출력 검증, immutable 선택·미확인 필드 | 알려진 차단을 Y/N 전에 반환. mapping/native/coverage를 아직 확인했다고 주장하지 않음 |
| 사용자 확인 | 동일 prepared snapshot 실행 여부 | Y/--yes는 이후 검사를 생략하지 않음. workspace 생성/획득은 확인 이후 |
| 실행 재검사 | source/선택 snapshot·도구/기능·파일 충돌/지원 전략, 누락 FPS의 기존 재검사 | 선택·FPS 의미 변경/필수 rate 미확인·unsupported plan을 해당 단계에서 실패. signed URL 갱신만으로 identity 검증을 대체하지 않음 |
| Acquire/Inspect | 소유 파일 획득,완전한 stdout/완료된 측정, O와 분리 축, native/local relation 및 확인 범위 | transfer 완료는 반환 가능하나 qualified input은 생성 금지. 원점/축 미확인을 Fast keyframe 제한으로 숨기지 않음 |
| Qualify/Plan | 모든 필요한 video frame/packet·audio boundary/decoder 준비와 source-native support coverage, 모드 전략·clock 표현 | mapping/coverage 모순·획득 누락·ambiguous 필수 packet이면 processing/최종 확정 금지. 미지원 native/container는 명시 실패 |
| Process | 검증한 계획으로 staging 생성·종료 확인 | 오류/취소 원인 보존. 다른 화질/codec/mode·전체 다운로드 fallback 없음 |
| Verify | 기대 video 집합·PTS/표시 끝, audio sample 준비/보정/유효 skip/end·A/V 관계, container/header·불필요한 변화 없음 | raw PCM count·duration 문자열·r==avg 하나로 통과시키지 않음. 필요한 검사 미완료면 최종 확정 금지 |
| Finalizing/Commit | 검증 통과·미수락 취소·파일 충돌 재검사 후 기존 once-only 이동 | 이미 채택한 취소/늦은 취소·overwrite=false·소유 파일 cleanup 정책 유지 |

각 gate는 필요한 범위에 대한 판정이다. target 밖의 unknown packet을 전역 성공으로 지우지 않지만, 그 사실 하나가 확인된 target를 자동 실패로 만들지도 않는다. 해당 unknown이 decoder-history/preroll·K·필요 end 검사 범위에 들어오면 다시 차단한다. 원래 source 불연속과 acquisition 부족은 서로 다른 판정이다. 처음 후보는 verified single relation·연속 source 구간이며 범용 piecewise timeline 기능은 계획하지 않는다.

## 6. 기존 오류와 연결 — 새 enum 없이 구분

G4c-2의 `UnsupportedFormat, Input` 후보를 모든 media-plan 실패에 적용하지 않는다. 현재 FormatSelector의 **요청 해상도 미제공은 Input** 동작을 유지하고, 도구/codec/strategy·시간축 측정 조건 실패는 기존 integration의 Execution 분류에 연결한다. 확인 전에 차단하느냐와 CLI Input/Execution 분류는 같은 개념이 아니다.

| 상황 | 코드 / category | 안전한 세부 이유·구별 |
|---|---|---|
| 문법/의미상 요청 범위 위반·동일 기준 길이 초과 | 기존 InvalidInput/InvalidRange / Input | 단계가 실행 중이어도 요청 자체 범위 오류의 기존 의미 유지 |
| 요청 높이 미제공 | UnsupportedFormat / Input | 현재 FormatSelector 유지, quality fallback 없음 |
| metadata ID/selection/FPS 변경·잘못된 구조 | InvalidMetadata / Execution | 최초 Unknown rate 해소와 알려진 rate 변경을 구별 |
| 실행 파일 없음 / tool 비정상 종료 | MissingTool / Execution, ProcessFailed / Execution | executable 존재와 필요한 encoder 지원은 다름. generic stderr 단어만으로 source 접근 실패를 추정하지 않음 |
| 근거가 있는 미지원 codec/container/native/end/숫자 표현 전략·필수 기능 부재 | UnsupportedFormat / Execution | `UnsupportedStrategy`, `NativeTimeUnsupported`, `RequiredCapabilityUnavailable` 같은 안전한 reason 후보. 처리 성공을 시도한 뒤 실패한 결과 검증과 구별 |
| 관계/원점/shared axis/필요 coverage 미확인·모순·실측/native facts 충돌 | VerificationFailed / Execution | `OriginUnverified`, `SharedAxisUnverified`, `MappingUnverified`, `CoverageIncomplete`, `NativeTimingContradicted`; 단순 미지원 포맷의 추측으로 숨기지 않음 |
| 필요한 frame rate를 추가 검사 후에도 결정 못 함 | UnknownFrameRate / Execution | cadence Unknown과 rate Unknown을 합치지 않음; 임의30fps 없음 |
| staging membership/shift/유효 skip/end/header 검사 실패 | VerificationFailed / Execution | `VideoMembershipMismatch`, `AudioBoundaryMismatch`, `PresentationEndMismatch`, `CodecInitializationInvalid` 같은 reason 후보 |
| timeout / root·child·stream 종료 미확인 | TimedOut / Execution, ProcessTerminationFailed / Execution | 종료 미확인 우선, 원래 원인·잠재 사용 중 소유 파일 보존 |
| 사용자 취소 / 쓰기·최종 이동 실패 | 기존 Cancelled, OutputFailed/OutputConflict | 취소는 오류 코드가 아님. 기존 cleanup/exit 정책 유지 |

reason 이름은 내부 진단 후보이며 이번에 Error 필드나 enum을 확장하지 않는다. 현재 OperationError의 Code/Category/안전한 Message/Field로 표현할 수 있다. raw JSON·서명 URL·요청 header를 Error에 넣지 않는다. 성공 이전 stage를 실패한 뒤 staging/final 파일을 조용히 성공으로 채택하지 않는다.

## 7. 실험 근거의 연결과 거절 사례

| 기존 근거 | 통합하는 사실 | 보장 밖/거절 예 |
|---|---|---|
| G1 | movie presentation O와 demux F 구별, signed seek 좌표 | raw priming packet min을 O로 채택·seek 성공만으로 값 보존 주장 |
| G2 | stream별 B·scope·required packet/frame coverage 분리 | target overlap packet 제거·틀린 B·source gap을 acquisition 부족과 혼동 |
| G3a~c | decoder-history 한계, source-grid 보정·native ceil·video membership/표시 끝·metadata tail | 1LSB 포괄 허용·임의 first available STARTPTS·wrong-unit atrim·padding rawcount 판정 |
| G4a-2 | K와 공통 render shift·native 표현·actual ends | 비정렬 K·독립 stream zeroing·setts 숫자 표현 미확인 |
| G4b-1/1-2 | VP9 header initialization·한정 native Opus 관계·skip/PCM gate | profile/pix_fmt 추정·고정+24·lacing/track-scale/complex padding 무검증 지원 |
| G4b-2a/2b | 137/140 original init/target·group/content/native edit bridge | 단순 두 시작값0·HLS media와 DASH movie 축 교체·edit 이중 적용 |
| G4c-1/2 | inspected cadence scope·movie clock 표현·codec capability≠sample/time 성공 | r==avg CFR 단정·1ms CHZZK 가설을 tolerance로 채택·decode 성공인 단순 copy를 승인 |

[G1](PHASE_1_G1_ORIGIN_SEEK_RESULT.md), [G2](PHASE_1_G2_COVERAGE_RESULT.md), [G3a](PHASE_1_G3A_AUDIO_VALUES_RESULT.md), [G3c](PHASE_1_G3C_AV_BOUNDARIES_RESULT.md), [G4a-2](PHASE_1_G4A2_FAST_RESULT.md), [G4b-1-2](PHASE_1_G4B12_OPUS_NATIVE_RESULT.md), [G4b-2b](PHASE_1_G4B2B_SHARED_AXIS_RESULT.md), [G4c-1](PHASE_1_G4C1_CADENCE_RESULT.md), [G4c-2](PHASE_1_G4C2_CODEC_BOUNDARIES_RESULT.md)의 범위를 그대로 유지한다. native Opus 로컬 성공을 모든 원격 Opus 지원으로 확대하지 않는다.

## 8. 남은 G5b와 구현으로 넘길 검증 단위

G5b에서 최소 CLR/API/failure 타입·필수 기능 preflight·FPS 미확인 준비/확인후 검사·최초 지원 acquisition/container/processing 전략을 구체화한다. 빈 target video/audio 집합·valid audio support 밖 지연/끝·decode 준비의 확인/거절 규칙·lossy 값 차이의 보증 범위도 정리해야 한다. 이 미정 항목을 현재 계약안으로 추정 확정하지 않는다. 미확인 전략은 구현 범위에서 명시 제외할 수 있지만 요구를 충족하는 양 사이트 경로의 검증 계획은 남겨야 한다.

이후 시작 조건 audit에서 채택 규칙↔실제 모델↔지원 전략↔테스트 oracle를 연결한다. 문서 준비 완료와 각 제품 단위 구현/검증 완료를 따로 표시한다. 6단계 순서를 유지하며 4단계 내에는 다음 의존 순서로 logical commit을 계획한다: shared signed/native/result 기본형 → acquisition 계약/측정 어댑터 → local probe 계약/구현 → qualification/모드 처리 계약 → Fast/Accurate 전략과 결과 검사. 실제 번호·파일 배치/검증 묶음은 G5b에서 확정한다. 현재 누락된 공용 타입을 위한 선행 단위가 필요하다는 사실만 정리했다.

구현 검증은 공백 모델 테스트가 아니라 변환/ceil의 부호·소수 FPS/clock overflow, scope와 identity 불일치 차단, missing PTS/packet/decoder history 차단, source-gap만 보정, metadata 유효 끝, Fast/Accurate source/output 관계를 확인해야 한다. 기존 known-good/negative control를 oracle로 삼되 실제 출력을 다시 실행하지 않고 fixture 성공을 media 통합 성공으로 보고하지 않는다.

이번 단위는 코드/문서 확인·기존 evidence의 좌표/ceil/negative gate 문서 검산만 수행했다. 새 프로젝트·media 생성/다운로드·도구 설치·제품 코드·build/test·Git staging/commit은 수행하지 않았다. 새로운 미디어 실험 성공을 추가하지 않으며 구체 검산 결과는 연결 JSON에 남긴다. 제품 4단계·Phase2·WPF 다운로드 UI·ClipEditor는 진행하지 않았다.
