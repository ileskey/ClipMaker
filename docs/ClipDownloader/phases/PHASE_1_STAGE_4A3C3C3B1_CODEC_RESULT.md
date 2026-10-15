# Phase 1 4a-3c-3c-3b-1 codec/decoded audio 관찰

실행일: 2026-10-13 (Asia/Seoul). 시작 HEAD `1d7d2f8245ea4a73e33524c876d75165a7c6cc13`, branch `phase/1`, clean. [fragment 관찰](PHASE_1_STAGE_4A3C3C3A_FRAGMENT_RESULT.md) 커밋을 확인했다. Base `0759dc52475538df848f765ce30c005cf755107f` 유지.

상태: **초기 참조의 FFprobe codec/decoded audio parameter 관찰 구현·관련 검증 완료**. 4a-3c-3c-3b를 논리적 커밋으로 나눈 첫 단위다. codec 설정 bytes·packet skip/priming·sample별 container presentation·native relation/source origin gate 구현 완료가 아니다.

## 구현과 관찰 경계

Infrastructure internal stream 관찰에 `InitialCodecObservation`을 추가했다. 기존 InitialReferenceProbe의 제한된 local FFprobe 명령·동일 hash/read lock 경로에서 codec_name/profile/pix_fmt/sample_fmt/sample_rate/channels/extradata_size를 요청한다. decoded audio frame의 sample_fmt/sample_rate/channels도 optional 별도 관찰로 받는다. 기존 exact PTS/time_base·duration/sample count·track/fragment·종료/출력 상한은 유지한다. public API·채택 정책·format 선택은 변경하지 않는다.

- 누락된 label/숫자 필드는 null이다. label의 명시적 `unknown`/`N/A`도 null이다. codec 이름만으로 profile·sample format·channels를 기본값으로 만들지 않는다. time_base 분모를 sample rate로 추정하지 않는다.
- stream와 frame의 sample parameters는 별도 보존한다. 둘이 다를 때 한쪽으로 덮거나 parse 단계에서 동일하다고 가정하지 않는다. 실제 지원 계획의 일관성 판정은 후속 gate에 남아 있다.
- 숫자는 exact Int32 범위이며 sample rate/channels는 present이면 양수, extradata_size는 비음수다. present0 sample rate/channels·negative·fractional·overflow·null·잘못된 type은 현재 제한 경로에서 InvalidMetadata/Data=null이다. zero를 유효 rate로 허용하지 않는다. extradata_size0과 누락은 구별한다.
- label은 최대128 characters의 ASCII letter/digit 및 ` _().:+-`만 허용한다. control·raw URI 형태·잘못된 type/길이를 거절하며 raw 값을 오류 메시지에 복사하지 않는다. 이 schema는 범용 모든 codec/profile 출력 지원을 뜻하지 않는다.
- video stream/frame에 실제 audio parameters가 있거나 audio stream에 실제 pixel format이 있으면 거절한다. 기존 중복 필드·stream 연결/역할·bounded JSON 검사를 재사용한다.
- extradata_size는 보고된 크기뿐이다. AVC configuration/SPS/PPS·AAC AudioSpecificConfig·container stsd·암호화/codec 지원을 읽거나 검증한 결과가 아니다. name/profile/sample format의 관찰 성공을 native/decoder 준비/지원 성공 fact로 승격하지 않는다.

packet skip_samples/discard_padding·decoder errors/history·container trun/trex presentation은 아직 관찰/대조하지 않았다. codec profile이나 decoded sample 수만으로 priming/유효 시작/끝·CFR·sample coverage/source O를 판정하지 않는다. native basis와 Verified fact는 여전히 발행하지 않는다.

## 실제 검증

| 실행 | 결과 |
|---|---|
| Downloader Infrastructure tests `--no-restore` | exit0, 통과487/실패0/건너뜀0 |
| `dotnet build ClipMaker.slnx --no-restore` | exit0, 경고0/오류0 |
| 보존된 local fixture + 실제 ToolLocator/ProcessRunner/FFprobe8.1 | Succeeded,213 frames·codec/decoded audio 관찰 반환 |

새20개 사례는 video/audio codec parameter·profile·extradata0/누락 구별·unknown label·sample rate 미상·stream/frame 값 차이 보존·type/range/role·label 상한/control·중복/실패Data를 검사했다. 기존 hash/lock/fragment/track/실행 수명 테스트를 포함한487개가 통과했다. 새 테스트/빌드 실패나 경고는 없다. Core/Media.Infrastructure 전체 테스트는 합산하지 않았고 WPF는 빌드만 검사했다.

실제 검증은 이전 `.artifacts/initial-probe-real/<GUID>/initial.mp4` 2,050,886bytes를 read-only 재사용해 sandbox 밖에서 실행했다. reflection harness의 reference/range/format label은 local fixture 입력이고 remote 선택/지원/coverage 증명이 아니다. 원본·복사본을 변경/삭제하지 않았고 새 network·영상 다운로드·설치·프로젝트 생성은 없다.

| 관찰 | 값 |
|---|---|
|video stream|h264 /High /yuv420p /extradata_size54 |
|audio stream|aac /LC /fltp /48000Hz /2 channels /extradata_size2 |
|첫 decoded audio frame|pts95935 /duration1024 ticks /nb_samples1024 /fltp /2 channels |
|첫 decoded audio frame sample_rate|출력 없음 →null |

첫 audio frame의 sample rate 누락을 stream의48000이나 time_base1/48000로 메우지 않았다. 이 한 파일의 decoded sample count/clock 일치를 native priming이나 codec 설정 검증으로 일반화하지 않는다.

## 다음 단위·커밋

다음 **4a-3c-3c-3b-2**는 bounded packet timing/skip·padding 관찰을 같은 파일 identity에 연결하는 단위다. decoded frames와 packets를 혼동하지 않고, explicit PTS/DTS 부재와 side data 부재를 보존해야 한다. 이후 codec 설정/container sample timing·native relation/미확인 gate, initial source origin/FPS inspection·target acquisition 연결과 전체 지원 검증이 남는다. Phase2·WPF 다운로드 UI·ClipEditor는 진행하지 않는다.

이번 커밋 범위는 Infrastructure 기존3·신규 test1·결과/설계/source 계약/baseline4문서다. staging/commit은 하지 않았다. 권장 메시지: `feat: Preserve initial codec and decoded audio observations`.
