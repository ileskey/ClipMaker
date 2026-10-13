# Phase 1 G5b API·초기 전략·구현 단위별 시작 조건

작성일: 2026-10-12 (Asia/Seoul). 시작 HEAD `bf95fd2b3e4182fb5059bf45c3416b89da113b02`, branch `phase/1`, clean. Phase Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **G5b 최소 API/전략 경계·단위별 readiness 정리 완료**. 바로 구현 가능한 **4a-1 exact signed 시간 기본형**과 후속 전략의 추가 검증을 구별한다. 전체 처리 설계 확정·양 사이트 Fast/Accurate 지원·전체 준비/제품 구현 완료는 아니다. [G5a](PHASE_1_G5A_DESCRIPTOR_GATE_DRAFT.md), [기존 계약](PHASE_1_CONTRACT_INTEGRATION.md), [검토 기록](evidence/PHASE_1_G5B_READINESS_REVIEW.json)을 함께 읽는다. 기존 일반 정책·G3c 처리 의미를 다시 승인받지 않는다.

## 1. 4a-1의 구체 시간 API와 구현 경계

기존 Media.Core/테스트 프로젝트·net10.0·System.Numerics를 사용한다. 새 패키지·프로젝트·솔루션 등록은 필요 없다. 아래는 **계획 API**이며 아직 소스에 없다. namespace는 기존 `ClipMaker.Media.Core.ValueObjects`다.

| 타입 | public 의미/API 계획 | 불변식·실패 |
|---|---|---|
| SignedMediaTime | BigInteger Numerator/Denominator, Zero, `TryCreate(n,d,out value,out TimeValidationError)`, `FromTimecode(Timecode)`, +/−, CompareTo, exact 값 동등성 | denominator>0, gcd 정규화·0/1. negative numerator 허용, negative/zero denominator 거절. 검증된 Timecode의 성분/FrameRate로 직접 계산 |
| NativeTimestamp | Int64 Pts·SignedMediaTime TimeBase, `TryCreate(pts,timeBase,out value,out error)`, PresentationTime | TimeBase>0, PTS negative/0 허용. missing PTS는 nullable 관찰에서 유지하며 factory에0으로 대체하지 않음 |
| PresentationRange | SignedMediaTime Start/End, `TryCreate(start,end,out value,out error)`, half-open membership | End>Start, negative 시작 허용. 사용자 MediaTimeRange의 FPS/nonnegative 규칙을 바꾸지 않음 |
| NativeTimestampRounding | Exact/Floor/Ceiling | TimeSpan nearest-even과 혼동하지 않음 |
| native 변환 | SignedMediaTime의 `TryToNativeTicks(timeBase,rounding,out long ticks,out error)` | Exact에서 fractional이면 OutOfRange, Int64 밖이면 Overflow. 모든 나눗셈/ceil/floor는 signed exact arithmetic |

TimeValidationError의 기존 OutOfRange/Overflow와 안전한 Field를 재사용하며 새 Application 오류 enum을 만들지 않는다. null 객체/정의되지 않은 rounding은 기존 객체 API 관례의 프로그래밍 오류다. 모든 외부 값은 factory에서 검증한다. 모든 컬렉션 snapshot·검증된 객체는 후속 계약에서도 불변으로 유지한다.

4a-1은 **3개 value object + rounding + 해당 로직 테스트**만 구현한다. 제품 파일은 `ValueObjects/{SignedMediaTime,NativeTimestamp,PresentationRange}.cs`, rounding enum은 NativeTimestamp.cs에 함께 둔다. 기존 Core.Tests에 동일 이름의 Tests.cs 3개를 추가한다. MediaOperationResult·source descriptor·acquisition/probe/service skeleton·도구 옵션·미디어 생성은 같은 커밋에 넣지 않는다. 필요한 arithmetic만 추가하고 범용 arbitrary-unit algebra·parser·serializer·UI 옵션을 만들지 않는다. `Timecode.TryToTimeSpan`이나 private exact 필드 공개로 기존 타입을 재작성하지 않는다.

## 2. 뒤이어 연결할 CLR/API 표면

아래 표는 후속 단위의 책임/입출력 계약이다. 긴 property별 생성자·parser와 adapter 파일 배치는 해당 단위에서 최소화한다. 구현되지 않은 시그니처를 컴파일/동작 검증했다고 주장하지 않는다.

| 계약 | 입력 → 반환 | 제한·상태 |
|---|---|---|
| ISelectedSourceInspectionService.InspectAsync | SelectedSourceInspectionRequest(JobId, immutable source/format snapshot, inspection purpose, workspace 소유 작업폴더, bounded 검사 budget) → OperationResult<SelectedSourceInspection> | 실행 확인 후 원점/분리축/native facts 또는 누락 FPS 조사. validated 시간 범위·ClipJob을 필수 입력으로 요구하지 않음. signed URL/HTTP 내부 상태는 노출하지 않음 |
| IClipDownloadService.AcquireAsync | AcquisitionRequest(ClipJob의 확정 선택/MediaTimeRange, 같은 source inspection snapshot, 소유 작업폴더) + acquisition progress/token → OperationResult<AcquiredMedia> | 기존 획득 계약 유지. 실제 획득/원점/각 relation/coverage의 상태와 검사 범위를 반환. 성공은 파일 획득 snapshot이며 qualification 성공이 아님 |
| IMediaProbe.ProbeAsync | MediaProbeRequest(local 소유 파일, Summary/필요 native PTS·samples 검사 범위) → MediaOperationResult<MediaProbeResult> | 기본 로컬60초/JSON32MiB, incomplete/누락값·정수 time_base·관찰 필드를 보존. 같은 inspect/probe를 근거 없이 반복하지 않음 |
| IMediaProcessingService.ProcessAsync | MediaProcessingRequest(MediaInput, 검증된 StreamProcessingPlan들, mode, staging 절대경로) + progress/token → MediaOperationResult<MediaProcessingResult> | 플랫폼/source URL/format IDs 없이 native range·translation·audio boundary·strategy를 처리. 반환은 staging·Copy/Trim/Mux/Encode 내용/제한이며 최종 성공 아님 |
| Application qualification/verification | source/acquired/probe snapshots + 요청/지원 전략 → qualified input·OutputExpectation 또는 기존 OperationError | 파일/stream/snapshot과 scope 일치 검사. 새 public service를 기계적으로 늘리지 않고 Application 내부 순수 validator로 시작 가능 |

inspection purpose는 FrameRate/SourceTimeline 의미를 구별한다. budget은 bytes·검사할 presentation 범위가 유한한 내부 정책 값이며 무제한 요청이 아니다. 기존 실험의8MiB/30초를 사용자 clip 전체의 고정 상한으로 승격하지 않는다. concrete source strategy의 실제 cap·chunk/초기화 경로는 구현 전에 명시하고 통합 검증한다. 초과 시 전체 파일 fallback·자동 무한 확대를 하지 않는다.

Media.Core의 `MediaOperationResult<T>`는 Succeeded(Data)/Failed(MediaFailure)/Cancelled 중 하나, Failed/Cancelled의 Data는null이다. `MediaFailureKind` 후보는 MissingTool/UnsupportedInput/InvalidMedia/ProcessFailed/TimedOut/TerminationUnconfirmed/OutputFailed이며 안전한 Message/Field를 둔다. raw ProcessResult·원시 JSON·Process 객체를 포함하지 않는다. Downloader 경계에서 기존 MissingTool/UnsupportedFormat/VerificationFailed/ProcessFailed/TimedOut/ProcessTerminationFailed/OutputFailed로 변환한다. Application OperationResult를 공유 Core에서 참조하지 않는다. 이 공통 결과형 구현은4a-1 이후 별도 계약 단위다.

## 3. FPS 미확인 준비와 실행 순서

Prepare는 입력 성분·metadata/선택·알려진 오류·출력 경로/지원 기능을 검사한다. FPS가 없으면 `TimecodeInput`을 보존하고 validated MediaTimeRange/ClipJob을 만들지 않는다. 확인 화면에서 FPS·정밀 구간 계산이 미확인임을 표시한다. 구간 획득/작업폴더 생성은 하지 않는다.

Y/--yes 후 workspace를 만들고 실행 재검사를 한다. FPS가 미확인인 경우 **별도 selected-source inspection**의 bounded 초기/관찰 구간에서 필요 rational rate를 조사한다. 이 요청은 ClipJob·FPS 계산된 target 없이 실행할 수 있으므로 순환 의존이 없다. inspected cadence는 rate 선택과 다른 사실이다. 임의30fps·소수값의 표준 rate 추정은 금지한다. 선택 FPS 정책을 적용한 뒤 기존 Timecode/MediaTimeRange·metadata duration 검증을 마쳐 ClipJob을 만들고 target acquisition으로 진행한다.

확인된 metadata rate가 probe와 충돌해 입력 FF의 의미가 바뀌면 조용히 재계산하지 않고 InvalidMetadata/Execution으로 차단한다. 최초 Unknown rate를 해소한 것은 이 변경과 구별한다. 추가 검사 후에도 사용할 rate가 없으면 UnknownFrameRate/Execution이다. known rate는 검사 결과/선택 일치만 재검사하며 임의 새 값을 채택하지 않는다. asset/format/native 관계가 바뀌면 기존 inspection/acquisition snapshot을 재사용하지 않는다.

## 4. 첫 전략 후보와 preflight

| 후보 | 지금 있는 근거 | 구현/통합 gate·readiness |
|---|---|---|
| CHZZK 선택 H.264/AAC | G1/G2 한source 두format의 초기 O/F·각 B·target coverage, G3c 경계, G4a-2 Fast | 단일 연속 관계·필요 초기/target source 참조와 parameter 확보. 제품 adapter/취소/retry·native/end 처리 구현은 아직 없음 |
| YouTube 선택137/140 | G4b-2 원본 init/SIDX bounded 참조·movie edit·공통 관계와 coverage | explicit selected pair를 유지하고 원본 bytes/observations로 재검증하는 adapter 필요. 0만으로 common axis 확정 금지. 모든 YouTube format 지원 아님 |
| Fast H.264/AAC → MP4 | 제한된 K/common shift·native sample 표현·actual ends | first 구현 후보. K/source/native/clock·codec skip/end·output gate 확인 범위 밖이면 명시 거절 |
| Accurate → libx264/AAC MP4 | G3 경계/재구성·G4c PTS/clock·3종 decode/encode capability | sample/time 의미와 lossy 출력 byte 동등성은 분리. **codec decoder preparation gate의 생산 경로는 추가 검증 필요**; 모든 AAC 경로가 준비됐다고 선언하지 않음 |
| VP9/Opus·AV1/Opus | G4b/G4c 제한된 로컬 capability·일부 native 보존 | 초기 범용 원격 처리 전략으로 승격하지 않음. 미지원 native/end/원격 relation은 UnsupportedFormat, 필요한 관계가 미확인/모순이면 VerificationFailed. Best 재선택 없음 |

clip preflight는 현재 ToolDiagnosticRequest로 Fast의 MP4 muxer, Accurate의 MP4 muxer/libx264/aac를 검사할 수 있다. feature listing Passed는 실제 encode 성공이 아니다. decoder availability는 현재 ToolFeatureKind에 없으며 probe/decode 실행 결과로 판단한다. 새 Encoder/Decoder enum을 미리 추가하거나 이름 존재만으로 모든 profile support를 보장하지 않는다. 구체 bitstream filter·filter/옵션과 숫자 표현은 계획 binder/실제 출력 검증에서 확인한다.

원격 획득/FFmpeg 처리에는 채택한 **고정 timeout 없음+취소**를 유지한다. 실험 하네스60초를 제품 제한으로 복사하지 않는다. 기능15초/metadata120초/로컬probe60초/종료정리10초·JSON32MiB와 기존 yt-dlp 단위 retry/fragment skip 금지 정책을 그대로 쓴다. yt-dlp 외 HTTP 어댑터의 요청/취소·byte completeness/retry 정책도 구체 구현 전 명시해야 하며 새 전체 Application retry를 추정 추가하지 않는다.

## 5. empty target와 audio support의 처리 경계

새 사용자 시간 정책을 추가하지 않고 **현재 계획으로 만들 수 없는 출력의 거절**을 구체화한다. `[IN,OUT)` 문법/시간 유효성과 선택되는 media 집합의 존재는 별개다.

- Accurate의 valid target 안에 source video PTS가0개이면 현재 video MP4 전략은 UnsupportedFormat/Execution(`EmptyVideoSelection`)으로 거절한다. valid 시간 구간을 InvalidRange로 바꾸거나 범위 밖 frame을 끼워 넣지 않는다. 필요한 관찰 누락 때문에0개인지 모르겠다면 VerificationFailed다.
- Fast는 target 내부 PTS가0개라도 확인된 선행 frame의 표시 범위/keyframe·coverage가 target을 포함하고 정상 output을 만들 수 있는 별도 계획이면 판단 가능하다. Accurate의0-frame 거절을 Fast에 기계적으로 적용하거나 자동 mode 변경하지 않는다. 이 Fast 사례의 실제 출력은 아직 미검증이다.
- original source audio가 확인된 Absent이면 기존 무음 video 출력 허용을 유지한다. Unknown/획득 누락을 Absent로 바꾸지 않는다. Present인데 target과 valid audio support가 전혀 겹치지 않는 경우는 **초기 전략의 미지원**으로 남기며 UnsupportedFormat/Execution(`EmptyRequiredAudioSelection`)이다. 임의 silence 또는 audio track 누락을 성공 처리하는 새 일반 정책은 채택하지 않는다.
- 부분 support는 source 시작 지연/끝을 유지한다. requested indices `[n_IN,n_OUT)`와 verified valid support `[n_FIRST,n_LAST)`의 교집합을 실제 선택한다. render 기준 Qₐ는 requested ceil boundary 그대로다. 첫 available sample로 Qₐ를 재설정하지 않는다. 내부의 verified source gap만 zero fill하고 support 바깥을 source gap으로 분류하지 않는다. 필요한 support/end가 미확인이면 VerificationFailed다.

G3c의 sample-grid 정의에서 실제 첫 output은 `Gₐ+max(n_IN,n_FIRST)/R−Qₐ`, 끝은 `Gₐ+min(n_OUT,n_LAST)/R−Qₐ`다. 이 부분-support 식은 계약 arithmetic 검산이며 새 미디어 성공 실험은 아니다. 최종 codec/edit/decode 출력 사례를 처리 단위의 통합 검증에 넣는다.

## 6. decoder 준비와 lossy 값 보증

`DecoderPreparation`은 준비 범위/유효 dependency의 Verified/Unknown/Contradicted와 안전한 근거를 가진다. 단순히 packet/PTS/nb_samples가 있거나 N초 preroll을 확보했다는 사실로 Verified를 만들지 않는다. full source decode의 PCM byte 일치와 필요한 boundary state의 준비 여부도 서로 다른 관찰이다.

현재 G3a는 초기 startup transient·AAC PNS/history 차이 반례를 남겼고 보편적인 준비 길이/허용치를 검증하지 않았다. 따라서 codec/strategy가 확인한 제한 범위에서 boundary preparation을 증명할 수 있을 때만 Accurate 계획을 qualify한다. 그러한 adapter/증거가 없으면 VerificationFailed(`DecoderPreparationUnverified`)이며 임의1LSB/몇sample tolerance·전체 다운로드/decode fallback은 없다. **이 검증 생산 경로가 남은 일이라는 사실을 결과 타입으로 가리지 않는다.**

source sample-grid 재구성의 lossless 값 검사는 G3b/G3c oracle를 유지하고, lossy AAC output PCM의 byte 동일성을 성공 기준으로 요구하지 않는다. 이것이 초기 상태 부족을 허용한다는 뜻은 아니다. phase/boundary/skip/end·A/V는 별도 exact 기대값으로 검사하며 값 보증 범위/미확인 항목을 전달한다. 추가 품질 threshold·PNS detector·decoder state snapshot 구현은 이번 기본형 범위에 넣지 않는다.

## 7. 4단계의 논리적 커밋·검증 순서

기존4a 획득→4b 검사→4c 처리 순서를 유지하고, 의존 타입이 없는 점을4a 안의 선행 커밋으로 해결한다. 순서대로 **하나씩** 진행한다. 2026-10-14 최신 사용자 지시에 따라 논리적 단위의 자동 커밋·검증 기록은 유지하고 개별 커밋 보고는 생략하며, 4단계 완료 후 통합 보고한다. 아래는 계획이며 완료 표시가 아니다.

| 단위 | 변경·책임 | 의미 있는 검증 / 시작 상태 |
|---|---|---|
| **4a-1** | Media.Core SignedMediaTime/NativeTimestamp/PresentationRange/rounding, 기존 Core.Tests의 해당 tests | 부호·gcd·Timecode exact bridge·29.97≠30000/1001·half-open/ceil/floor·fractional Exact 실패·Int64 overflow·G2/G3c arithmetic. **이 단위 준비 완료**, product 구현은 없음 |
| 4a-2 | 공유 MediaOperationResult/관찰/native 기본 모델, Application selected inspection/acquisition 계약·immutable snapshots | 상태/실패Data 불변식·absence/unknown·scope/snapshot 불일치·source/native 이중 보정 차단. 계약 구현 가능하나 actual adapter 통합 완료 아님 |
| 4a-3 | 선택 source inspection/획득 adapter·상한/소유 파일/선택 검증 | 초기+target bytes·정수 mapping/coverage·fragment completeness·실제 단위 retry·취소/종료·source 변화·HTTP 범위 거절. HTTP 구현 정책/최소 parser 범위 구체화 선행 |
| 4b-1 / 4b-2 | 공유 probe 요청/결과 계약, FFprobe 실제 구현/필요 native 해석 | PTS 부재·summary≠CFR·scope·raw/native·skip/end·header·JSON 상한·실제 local probe. parser 일반화 없이 지원 범위 밖은 실패 |
| 4c-1 | qualification·StreamProcessingPlan/OutputExpectation·지원 binder | K/translation·ceil/support·empty 집합·decoder 준비·필수capability·범위/clock 표현 차단; 허위 Verified 방지 |
| 4c-2 | Fast H.264/AAC 전략·staging/output 검사 | G4a-2 source/common shift·native/packet/content·actual ends·coarse 단순copy 반례·decode/취소, 성공file만 검사통과 아님 |
| 4c-3 | Accurate 전략·입력 sample 준비/재구성·output 검사 | G3c/codec-end/VFR 경계·lossless oracle·native delay·actual 표시끝. **decoder preparation의 확인/거절 사례를 추가 검증해야 완료 가능** |

source/미디어 data와 관련 adapter는 동일 단위의 fake 관찰 테스트→기존 로컬 oracle 실제 실행→해당 사이트 bounded source 통합 순서로 검사한다. 제품 tests는 실제 구현을 보호할 때 추가하고 빈 skeleton/명령 문자열을 그대로 복사한 테스트로 검증을 대신하지 않는다. build/test는 필요한 프로젝트와 최종 솔루션을 검사하되 문서 검토만으로 실행했다고 보고하지 않는다.

## 8. readiness audit와 다음 작업

| 조건 | 현재 판정 |
|---|---|
| branch/base·사용자 변경 보존·기존 테스트 프로젝트 | 확인 완료, clean `phase/1`, Base 유지. 이전451개 테스트는 과거 결과이며 이번에 재실행하지 않음 |
| 4a-1 타입/API·수학/검증 oracle·패키지 의존 | **준비 완료**. 현행 project/framework로 구현 가능, 설치/미디어 필요 없음 |
| 이후 acquisition/probe/API 책임·오류·일반 운영 정책 | 문서 연결 완료, 제품 구현/실제 검증 미완료 |
| 범용 source/native/codec/end 지원·HTTP adapter 운영 세부 | 미완료. 검증된 전략별로 구현 전 닫고 범위 밖은 명시 거절 |
| AAC Accurate decoder preparation·partial/empty output 통합 | 미완료. 새로운 전체 지원/수치 tolerance로 추정 확정하지 않음 |
| 4단계 전체·5Application/6CLI·양 사이트/모드 completion 검증 | 미완료 |

다음 사용자 구현 지시에 **4a-1만** 진행할 수 있다. 구현 후 기존 Core 테스트 전체(기존 시간 의미 회귀 포함)와 새 의미 검증을 실행하고, 솔루션 build로 연결을 검사한다. 기존 패키지를 쓰며 `dotnet test tests/ClipMaker.Media.Core.Tests/ClipMaker.Media.Core.Tests.csproj --no-restore`와 `dotnet build ClipMaker.slnx --no-restore`가 검증 명령 계획이다. 실패/실제 개수를 그대로 보고하고 451개 과거 전체 결과를 이번 결과로 대체하지 않는다. 최초 권장 commit은 `feat: Add exact signed media time primitives`다. 그 다음4a-2/4a-3…을 각 준비 조건과 함께 하나씩 진행한다. 기본형 구현을 기다리게 하는 별도 포괄 재승인은 요구하지 않는다. 준비된 기본형과 아직 미확인 처리 전략을 구별한다.

이번 실제 수행은 Git·현재 code/project·관련 채택 문서/실험 기록 확인, SDK 버전 조회, bounded contract arithmetic/현재 API·오류 이름·참조 해시 검토다. 새 product code·project·package·media/외부 미디어 도구 실행·build/test·staging/commit은 수행하지 않았다. 초기 검토 문서의 가칭을 구체 단위에 맞췄지만 전체 미정 전략을 구현했다고 보고하지 않는다. 결과/evidence·설계·source 계약·baseline5개가 이번 커밋 범위다. Phase2·WPF 다운로드 UI·ClipEditor는 진행하지 않는다.
