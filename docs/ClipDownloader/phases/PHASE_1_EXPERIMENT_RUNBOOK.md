# Phase 1 최소 실험 실행 절차·사전 판정 기준

작성일: 2026-10-10 (Asia/Seoul).

상태: **실행 전 준비 문서**. 명령은 재현용 계획이며 아직 실행하지 않았다. 필터 help·기능 목록 조회만 수행했다. 사용자 담당 yt-dlp 설치를 제외한 계약·절차 준비를 위한 산출물이다. 영상 생성/다운로드·검증 프로젝트/하네스 생성·제품 구현 승인을 뜻하지 않는다.

연결: [시작 점검](PHASE_1_PREIMPLEMENTATION_PLAN.md), [하위 계약 통합](PHASE_1_CONTRACT_INTEGRATION.md), [채택 설계](PHASE_1_DESIGN.md) 제6~8절. 보류 사항은 실행 결과로만 닫는다.

## 1. 입력·환경·산출물

- 기존 확인 환경: SDK 10.0.302, .NET/WindowsDesktop 10.0.10, FFmpeg/ffprobe 8.1 full build, node v22.14.0. node 경로는 fnm 세션 경로라 후속 셸에서 다시 탐색한다.
- 실제 FFmpeg bin: `C:/Users/shiro/AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-8.1-full_build/bin`. 실험 실행 시 경로·버전을 재확인한다. 사용자 설치 yt-dlp 절대경로·버전은 아직 없다.
- help 조회로 drawtext/testsrc2/aevalsrc/trim/atrim/setpts/asetpts/select/showinfo와 libx264/aac/mp4 기능 목록을 확인했다. `C:/Windows/Fonts/consola.ttf` 존재도 확인했다. 생성·인코딩 성공 증거는 아니다.
- 실행 시 저장소 `.phase1-experiments/<yyyyMMdd-HHmmss-Guid>/`에 inputs, calibration, outputs, evidence를 만들고 기존 폴더와 충돌하면 중지한다. 아직 폴더를 만들지 않았다. 제품 솔루션에는 등록하지 않는다. 생성물은 완료 commit에 섞지 않는다.
- 환경/도구 경로·버전·명령 배열·exit code·시작/종료 시각은 evidence/environment.md에 기록한다. 결과 표에는 Planned/Executed/Passed/Failed/Inconclusive/Blocked를 구분한다. 민감 JSON·서명 URL·쿠키는 보존하지 않는다.
- 기본 로컬 영상은 640×360, 40초, 48kHz mono PCM 기준 신호, H.264 영상으로 만든다. NUT를 기준 컨테이너 후보로 사용하되 probe에서 실제 time_base/PTS를 확인한 뒤 입력으로 채택한다. 생성 명령의 이상적 시간과 실제 mux 후 PTS를 혼동하지 않는다.

## 2. 실행 전 고정할 사례 표

| 사례 | 입력 | IN/OUT | 질문 |
|---|---|---|---|
| L1 | CFR 30/1 | 7.25 / 17.75초 | 키프레임과 어긋나는 경계·A/V origin |
| L2 | CFR 30000/1001 | 7.25 / 17.75초 | 소수 FPS·PTS quantization |
| L3 | CFR 60/1 | 7.25 / 17.75초 | 높은 FPS·프레임 수 |
| L4 | 0~20초 30fps, 이후 15fps VFR | 17.25 / 27.75초 | 평균 FPS와 실제 프레임 선택의 차이 |
| L5 | L1의 A/V PTS 모두 +5초 | 기준 원본 7.25 / 17.75초, 입력 PTS 기준 12.25 / 22.75초 | nonzero start_time·공통 origin |
| S1/S2 | 실제 CHZZK/YouTube 선택 포맷 | metadata 조회 후 각 10~30초 | 원본↔획득 시간축·스트림·실제 의존성 |

L1~L5 길이는 10.5초다. 이 값은 사이트 구간을 추정한 것이 아니며 타임코드 처리 테스트와 별도의 seek 실험 시간값이다. 각 실험에서 입력 PTS 기준인지 원본 페이지 시간 기준인지 표에 명시한다.

## 3. E1 로컬 입력·교정·처리 명령

아래는 실험 root를 작업 디렉터리로 한 **PowerShell용 명령 초안**이다. 실행 직전에 변수·디렉터리·출력 부재를 검증한다. 각 외부 명령 직후 `$LASTEXITCODE`를 기록하고 nonzero이면 후속 단계로 진행하지 않는다. `-n`으로 기존 파일을 보존한다. 실행 성공·구문 검증은 아직 수행하지 않았다.

```powershell
$phase1Ffmpeg = (Get-Command ffmpeg.exe -ErrorAction Stop).Source
$phase1Probe = (Get-Command ffprobe.exe -ErrorAction Stop).Source
$phase1Audio = 'aevalsrc=0.6*sin(2*PI*1000*t)*lt(mod(t,1),0.01):s=48000:d=40'
$phase1Label = "drawtext=fontfile='C\:/Windows/Fonts/consola.ttf':text='F%{n}':fontsize=30:fontcolor=white:box=1:boxcolor=black:x=12:y=12"
# 1초마다 흰 사각형으로 visual marker를 표시한다. frame label과 다른 위치를 사용한다.
$phase1Marker = "drawbox=x=500:y=20:w=100:h=100:color=white:t=fill:enable='lt(mod(t,1),0.0333333333333333)'"
& $phase1Ffmpeg -hide_banner -nostdin -n -f lavfi -i 'testsrc2=size=640x360:rate=30:duration=40' -f lavfi -i $phase1Audio -map 0:v:0 -map 1:a:0 -vf "$phase1Label,$phase1Marker" -c:v libx264 -preset veryfast -crf 18 -g 60 -keyint_min 60 -sc_threshold 0 -bf 3 -pix_fmt yuv420p -c:a pcm_s16le -f nut inputs/cfr30.nut
```

위 영상 입력의 rate를 30000/1001과 60으로 바꾸고 출력 파일명을 각각 cfr2997.nut/cfr60.nut로 바꾸어 L2/L3를 만든다. label은 모든 영상에 넣고 프레임 ID는 인코딩 전에 기록한다. visual marker의 실제 발화 PTS를 probe/표시 프레임에서 측정한다. 이상적인 정수초와 강제로 같다고 간주하지 않는다.

VFR은 30fps 30초/900프레임을 생성한 후 첫 600프레임은 30fps, 나머지는 15fps 간격으로 재배치한다. 별도 필터 문자열은 아래와 같다. VFR에서는 `-fps_mode:v passthrough -enc_time_base:v 1:90000`을 추가하고 audio는 40초로 유지한다. input/encoder/mux 단계에서 frame drop/duplicate가 있으면 기준 입력 실패다.

```powershell
$phase1VfrFilter = "settb=1/90000,setpts='if(lt(N,600),N/(30*TB),(20+(N-600)/15)/TB)',$phase1Label,$phase1Marker"
& $phase1Ffmpeg -hide_banner -nostdin -n -f lavfi -i 'testsrc2=size=640x360:rate=30:duration=30' -f lavfi -i $phase1Audio -map 0:v:0 -map 1:a:0 -vf $phase1VfrFilter -c:v libx264 -preset veryfast -crf 18 -g 60 -keyint_min 60 -sc_threshold 0 -bf 3 -pix_fmt yuv420p -fps_mode:v passthrough -enc_time_base:v 1:90000 -c:a pcm_s16le -f nut inputs/vfr.nut
& $phase1Ffmpeg -hide_banner -nostdin -n -i inputs/cfr30.nut -map 0:v:0 -map 0:a:0 -vf 'setpts=PTS+5/TB' -af 'asetpts=PTS+5/TB' -fps_mode:v passthrough -enc_time_base:v 1:90000 -c:v libx264 -preset veryfast -crf 18 -c:a pcm_s16le -f nut inputs/offset5.nut
```

L5를 재인코딩한 뒤 frame ID 유지·실제 양 스트림 +5초 offset을 probe한다. 명령 의도와 결과가 다르면 nonzero origin 시험 입력으로 사용하지 않는다. 모든 입력은 다음 summary/frame 검사로 확정한다.

```powershell
& $phase1Probe -v error -show_format -show_streams -of json inputs/cfr30.nut
& $phase1Probe -v error -select_streams v:0 -show_frames -show_entries 'frame=pts,best_effort_timestamp,pkt_dts,duration,key_frame,pict_type:stream=time_base,r_frame_rate,avg_frame_rate' -of json inputs/cfr30.nut
& $phase1Probe -v error -select_streams a:0 -show_packets -show_entries 'packet=pts,dts,duration,side_data_list:stream=time_base,sample_rate' -of json inputs/cfr30.nut
```

로컬 기준만 전체 40초를 검사한다. PTS 정수와 time_base의 유리수 곱을 기준값으로 저장한다. 작은 입력이라도 32MiB JSON 상한 초과 시 범위를 줄이며 무제한 메모리 수집을 하지 않는다. 파일 간 비교에는 DTS나 출력 순서의 packet 번호를 frame PTS처럼 사용하지 않는다. 필수 PTS가 없으면 그 입력/사례는 Inconclusive다.

각 입력에 대해 **알고리즘 비교 전에 별도 교정 출력**을 만든다. 동일 원본 전체를 동일 후보 인코더/컨테이너 설정으로 MP4 변환해 audio encoder delay·sample quantization·PTS 이동을 측정한다. 이 교정은 seek/trim 경로를 사용하지 않으며 검사 대상 클립 결과로 tolerance를 조정하지 않는다.

```powershell
& $phase1Ffmpeg -hide_banner -nostdin -n -i inputs/cfr30.nut -map 0:v:0 -map 0:a:0 -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -fps_mode:v passthrough -enc_time_base:v 1:90000 -c:a aac -b:a 128k calibration/cfr30.mp4
```

동일 A/V 기준으로 trim+encode와 input seek+copy 후보를 각각 비교한다. PCM audio를 MP4 copy할 수 있다고 가정하지 않으므로 copy 비교의 입력은 교정에서 생성한 MP4다. 그 MP4를 다시 probe해 시간축 기준을 기록한다.

```powershell
# 영상·음성을 같은 7.25초 origin만큼 이동한다. 각 스트림 STARTPTS를 따로 0으로 만드는 경로와 구분한다.
& $phase1Ffmpeg -hide_banner -nostdin -n -i inputs/cfr30.nut -map 0:v:0 -map 0:a:0 -vf 'trim=start=7.25:end=17.75,setpts=PTS-7.25/TB' -af 'atrim=start=7.25:end=17.75,asetpts=PTS-7.25/TB' -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -fps_mode:v passthrough -enc_time_base:v 1:90000 -c:a aac -b:a 128k outputs/l1-filter.mp4
& $phase1Ffmpeg -hide_banner -nostdin -n -ss 7.25 -i calibration/cfr30.mp4 -t 10.5 -map 0:v:0 -map 0:a:0 -c copy outputs/l1-copy.mp4
& $phase1Ffmpeg -hide_banner -nostdin -n -ss 7.25 -i calibration/cfr30.mp4 -t 10.5 -map 0:v:0 -map 0:a:0 -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -fps_mode:v passthrough -enc_time_base:v 1:90000 -c:a aac -b:a 128k outputs/l1-seek-encode.mp4
```

같은 설정으로 L2/L3, L4의 17.25~27.75초, L5의 입력 PTS 12.25~22.75초를 비교한다. L5의 input seek는 기본적으로 시작 timestamp에 상대적인 7.25초 후보와 `-seek_timestamp 1 -ss 12.25` 절대 timestamp 후보를 별도 실행·기록한다. 실제 옵션은 설치 버전 help와 대조한다. input seek로 잘라낸 파일의 0초를 원본 7.25초라고 전제하지 않는다. 이 명령은 제품 기본 경로 확정이 아니다.

각 output에 summary/frame/packet 검사를 반복하고 다음 decode 검사를 추가한다.

```powershell
& $phase1Ffmpeg -hide_banner -nostdin -v error -i outputs/l1-filter.mp4 -map 0:v:0 -map 0:a:0 -f null -
& $phase1Ffmpeg -hide_banner -nostdin -n -i outputs/l1-filter.mp4 -map 0:a:0 -c:a pcm_s16le -f s16le evidence/l1-audio.s16
# 경계 프레임·정수초 visual marker는 PNG로 확인한다. 자동 이미지 동일성 대신 표시된 F 번호를 대조한다.
& $phase1Ffmpeg -hide_banner -nostdin -n -i outputs/l1-filter.mp4 -vf "select='eq(n,0)+eq(n,1)'" -frames:v 2 evidence/l1-first-%02d.png
```

마지막 프레임·각 marker 추출은 probe에서 확인한 출력 frame ordinal을 select에 넣는다. 실험 판정용 비교 helper는 향후 격리된 실험 root에만 작성하고 제품 코드·테스트 프로젝트와 섞지 않는다. helper가 없으면 긴 전체 frame ID를 눈대중으로 통과시키지 않고 판정 보류다.

## 4. 모드별 판정 기준·음성 교정

- 입력 채택: 생성한 정수 frame ID가 단조롭게 보존되고 PTS가 확인 가능해야 한다. CFR은 검사 창 내 PTS 차이가 time_base 양자화 오차를 제외하고 일정함을 검사한다. 전체 40초에 대한 결과를 사이트 전체 CFR 보장으로 확대하지 않는다. VFR은 실제 서로 다른 간격을 확인한다.
- Accurate 영상: 원본 측정 PTS에서 `[S,E)`를 만족하는 frame ID 집합이 expected다. 첫/마지막·개수·중간 ID 순서가 일치해야 한다. 누락/중복/밖의 프레임이 있으면 실패다. 표시 손상이나 매칭 불확실이면 Inconclusive다. 재인코딩 pixel hash 일치는 요구하지 않는다.
- 이상적 0-origin 입력의 산술 예상은 L1 F218~F532/315개, L2 F218~F531/314개, L3 F435~F1064/630개, L4 F518~F716/199개다. 이는 사전 계산값이다. 실제 입력 PTS가 생성 의도와 다른 경우 입력부터 조사하고 결과를 통과시키려고 expected를 바꾸지 않는다. 최종 비교값은 채택한 원본의 정확한 PTS로 clip 실행 전에 고정한다.
- 시간 origin: 대응 프레임의 `sourcePTS-outputPTS`가 하나의 shift로 설명되는지 검사한다. 영상과 음성에 서로 다른 0초 기준을 임의 적용하지 않는다. container/encoder time_base quantization 합으로 설명되지 않는 차이는 실패다. 임의 몇 프레임 허용 범위를 넣지 않는다.
- 교정에서는 원본과 전체 MP4를 PCM으로 decode하고 48kHz pulse 구간을 normalized cross-correlation으로 대응시킨다. priming/skip/padding side data 및 decoder가 이를 적용했는지 기록한다. 고정 encoder bias는 **교정 파일에서만** 측정한다.
- pulse 검색은 각 1초 marker 주변 독립 window에서 수행한다. 단일 상관 peak가 식별되지 않으면 측정 실패로 둔다. 압축 후 amplitude threshold 하나로 onset을 판정하지 않는다. pulse 대응이 불명확하면 수치를 만들어내지 않는다.
- 교정 잔차의 절대 최대값을 `Rcal`, source/output timestamp의 반 tick 합을 `Q`, 1-sample lag의 최대 두 비교 오차를 `2/48000초`로 두고 `U = Rcal + Q + 2/48000`을 계산한다. 이 공식은 사전 고정하고 **Rcal 측정 후 clip 실행 전에** 수치 U를 evidence/criteria.md에 기록·고정한다. 입력/설정별 교정이며 다른 codec에 재사용하지 않는다.
- clip의 marker별 audio offset에서 교정 bias를 뺀 잔차가 U 이내인지 검사한다. 구간 앞/뒤 marker 잔차의 drift는 독립 두 측정 오차 합인 2U 이내여야 한다. video marker의 원본 PTS와 output PTS shift를 반영해 A/V 차이를 구한다. 영상 프레임 주기를 음성의 허용 오차로 대체하지 않는다.
- 음성 `[S,E)`의 decoded sample 범위도 비교한다. AAC packet duration만으로 audible 끝을 판정하지 않는다. clip 양 끝의 encoder padding·edit list와 decoded sample 수를 기록한다. 위 범위로 설명되지 않는 누락·offset/drift는 실패다. 교정 기준 자체를 못 세우면 Accurate A/V 성공을 선언하지 않는다.
- Fast: 실제 첫/끝 원본 frame, keyframe, 실제 길이·요청 편차를 기록한다. MP4 decode·원본 음성 유지·A/V 관계 설명 가능을 요구한다. Fast의 경계 오차를 Accurate 합격으로 간주하지 않는다. 플랫폼/프로토콜별 지원 한계는 관찰을 기록한 뒤 제품 전략에서 정한다.

U는 아직 측정된 수치가 아니며 제품 일반 허용 오차도 아니다. 교정 완료 후 기준을 고정할 수 있는 절차를 준비한 것이다.

## 5. E0/E2 yt-dlp 설치 이후의 절차

사용자가 설치를 맡는다. 실행 전에 절대경로·버전·help로 옵션 지원과 배포별 EJS/JS runtime 의존성을 확인한다. runtime 설치·remote component 자동 다운로드·쿠키 import는 하지 않는다. 조회 timeout은 전체 120초, 버전/기능은 호출당 15초로 실행 supervisor가 관리한다. socket-timeout은 전체 timeout 대체가 아니다.

```powershell
# 사용자 설치 이후: 실제 경로로 대입. 현재는 실행하지 않는다.
$phase1YtDlp = '<사용자 설치 yt-dlp 절대경로>'
& $phase1YtDlp --version
& $phase1YtDlp --help
# 아래는 설치 버전에서 옵션 확인 후 적용할 조회 인자 배열이다.
$phase1MetadataArgs = @('--ignore-config','--no-playlist','--skip-download','--dump-single-json','--retries','2','--extractor-retries','2','--fragment-retries','2','--file-access-retries','0','--abort-on-unavailable-fragments','--','https://chzzk.naver.com/video/15430558')
```

metadata stdout은 bounded collector로 parse 후 id/title/duration/state/formats의 필요 필드만 익명화 저장한다. 원시 JSON을 파일·로그로 tee하지 않는다. yt-dlp 버전별 JS 선택은 help와 버전에 맞는 공식 문서를 확인해 인자를 추가한다. node가 있다는 이유로 조회 가능한 것으로 간주하지 않는다. YouTube 입력은 `https://www.youtube.com/watch?v=sFXPM_sfJNo`다.

조회 후 길이·완료 상태를 확인하고 첫 구간은 영상 끝에서 여유를 확보한 10~30초로 고정한다. 중간 구간 우선이며 keyframe·protocol·source PTS 근거를 확인한다. 시작/끝과 선택 video/audio format ID를 criteria.md에 먼저 기록한다. **Best 정렬 확정 전에는 선택한 실험 포맷과 이유를 명시**한다.

구간 획득 인자 후보는 다음과 같다. 실행 요청 전 선택된 실제 값으로 치환하고 supervisor로 bounded output·취소를 관리한다.

```text
--ignore-config --no-playlist --retries 2 --extractor-retries 2
--fragment-retries 2 --file-access-retries 0 --abort-on-unavailable-fragments
--ffmpeg-location <확인한 FFmpeg bin> --format <videoID 또는 videoID+audioID>
--download-sections *<S>-<E> --no-overwrites
--output <run root의 outputs/site-id.%(ext)s> -- <정규화 페이지 URL>
```

제품에서 default merge/remux를 무엇으로 할지는 실험 결과를 보고 정한다. 이 실험에서는 획득 container·분리/병합·실제 trim/encode를 먼저 기록한다. force-keyframes/추가 reencode를 기본 인자로 넣지 않는다. 위임 FFmpeg 경로에서 retries가 적용되는 범위는 별도 기록한다.

사이트 frame 원본 대응은 작은 겹침 구간을 추가로 획득해 frame identity/PTS를 대조하되, 같은 방식으로 PTS를 재설정한 두 결과의 일치만으로 절대 원본 위치를 증명하지 않는다. 추출 프로토콜의 원본 PTS·위임 동작 등 독립 근거가 없으면 Accurate 시간축 Inconclusive다. 전체 다운로드·다른 품질/모드 fallback은 금지한다.

FPS가 없으면 metadata/manifest/원격 probe에서 선택 포맷의 유리수 FPS·PTS를 얻을 수 있는지 확인한다. 원격 probe는 로컬 probe의 60초 계약을 자동 확대 적용하지 않고 별도 실행 timeout/출력 제한을 먼저 정한다. 조회에 영상 데이터 읽기가 필요한지는 별도 기록하며 확인 전 구간 획득 금지에 맞는 Application 순서를 선택한다. 불확실하면 UnknownFrameRate로 닫을 수 있는 경로를 유지한다.

## 6. E3 프로세스 수명 spike와 2단계 검증

아래는 최초 실행 전 계획이다. 2b에서 기존 테스트 fixture를 확장해 실제 baseline/수명 실험·검증을 수행했다. 질문·수정된 가정·실패/통과와 최종 구현 전략은 [2b 실제 결과](PHASE_1_STAGE_2B_RESULT.md)를 따른다. 이 절의 ‘아직 생성하지 않았다’는 작성 당시 기록이며 현재 상태를 뜻하지 않는다.

제품 ProcessRunner 작성 전에 수명 전략만 검증하는 작은 .NET console 실험 fixture를 격리 root에 만들 계획이다. **아직 프로젝트·코드는 생성하지 않았다.** SDK 기존 runtime만 사용하고 패키지 설치·솔루션 등록은 하지 않는다. 생성/실행 요청 범위가 주어졌을 때 다음 프로토콜대로 작성한다.

- controller: shell=false/ArgumentList/redirected streams/CreateNoWindow, stdout/stderr 각 byte drain, monotonic deadline, CancellationTokenSource, kill(entireProcessTree:true), 별도 10초 cleanup. 결과는 내부 구조와 PID·시작 시각만 저장한다. 새 프로세스에 대해서만 종료 요청한다.
- worker mode는 `dual-output`, `utf8-boundary`, `long-record`, `exit-code`, `wait`, `spawn-hold`, `root-exit-hold`다. child는 동일 fixture를 `child-hold`로 시작한다. 자식 손자까지 생성하고 run ID·PID·시작 시각을 별도 control file에 기록한다. 기존 시스템 PID에 kill을 시도하지 않는다.
- dual-output: 두 스레드가 각각 40MiB를 8KiB chunk로 write/flush; UTF-8은 한글+emoji byte sequence를 1/2/3-byte 경계로 쪼개고 개행 없는 300KiB도 포함한다. 출력 데이터는 로그에 복제하지 않는다.
- exit-code: 0과 7, start 실패는 없는 파일과 실행 불가 파일. wait는 30초 대기하며 controller timeout 1초로 종료를 재현한다. 취소는 start 확인 후 1초로 요청한다. 해당 초값은 시험 재현 입력이고 제품 timeout 정책 변경이 아니다.
- spawn-hold: root/child/grandchild가 살아 있는 상태에서 취소. root-exit-hold: root는 0으로 끝나고 child가 inherited stdout/stderr write handle을 보유한다. 파이프 보유가 재현되지 않으면 시험 미성립으로 기록하고 passing case로 사용하지 않는다.
- controller는 root 종료·등록된 PID의 실제 종료·양 pipe EOF/정리를 별도 시각으로 기록한다. PID 재사용을 피하려고 시작 시각을 같이 확인한다. 별도 observer가 잔존 PID를 확인한다. 모든 증거가 없으면 정상 취소로 반환하지 않는다.
- 실패 사례의 cleanup은 실험이 생성한 PID/시작 시각에만 한정하고 최종 잔존 상태를 기록한다. 부모 선종료·파이프가 tree kill로 해결되지 않으면 최소 Job Object 방안과 비용을 비교한다. spike의 process 전략 성공과 제품 Runner의 byte 상한 구현 성공은 구분한다.
- 2단계 실제 Runner 테스트: 정확히 32MiB와 +1byte, 256KiB tail의 마지막 바이트, read/callback 예외, cancellation/timeout/exit 경합, cleanup 실패, 개행 없는 출력·UTF-8, long-record limit을 같은 fixture로 검증한다. 아직 없는 제품 Runner의 성공을 spike로 대신 선언하지 않는다.

## 7. 실행 기록 양식과 설계 반영

```text
Case ID / 실행 상태:
기준 commit / OS·SDK / 도구 절대경로·버전:
입력 생성 사양 / 입력 time_base·PTS·실제 FPS:
사이트·영상 ID / 선택 format·protocol·codec·음성 유무:
명령 인자(민감값 제거) / 시작·종료 시각 / exit code:
요청 S/E·시간 기준 / 기대 frame ID·개수:
실제 첫/마지막 frame ID·PTS·개수 / origin shift:
교정 bias·Rcal·Q·U / 고정 시각:
음성 residual·drift / sample 범위·priming/padding:
root/child/pipe 종료 시각 / remaining paths:
판정과 근거 / 미확인·제한 / 영향을 받는 계약:
```

결과는 상세 설계의 실제 수행 기록에 연결한다. 시간축 관계·FPS 순서·Best/코덱·자식 수명 보완을 갱신하고 통합 계약을 다시 대조한 뒤 전체 준비 완료를 판정한다. 사이트가 막히면 실행 못한 이유와 차단 범위를 남기며 Phase 1 완료로 표시하지 않는다. 이 문서 작성 시 실험 결과는 없다.

공식 근거: [FFmpeg seek·옵션](https://ffmpeg.org/ffmpeg.html#Main-options), [trim/atrim·PTS 필터](https://ffmpeg.org/ffmpeg-filters.html), [ffprobe 검사 옵션](https://ffmpeg.org/ffprobe.html), [yt-dlp 옵션](https://github.com/yt-dlp/yt-dlp#usage-and-options), [Process.Kill](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill?view=net-10.0). 옵션 지원은 실제 설치 버전 help와 대조한다. 문서만으로 사이트의 원본 시간축이나 자식 종료를 확정하지 않는다.
