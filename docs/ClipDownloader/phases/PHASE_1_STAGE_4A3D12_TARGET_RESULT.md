# Phase 1 4a-3d-12 indexed target acquisition and bounded Accurate prefix

2026-10-14, 시작 HEAD614b79f, phase/1, Base 유지.

indexed target을source service에연결했다. metadata·선택재검사→초기137/140/관계재검사→양쪽first file SHA/원점/FPS일치→새source index→whole 직접참조수집→실제native sample검사→변하지않은codec/clock/movie header와index/native 구간및필요support 대조→acquisition snapshot/mapping 순서다. 초기provider 관계와변하지않은global affine movie edit를source 시간근거로사용하며target의실제native/index 관계는독립적으로검사한다. index0이나tfdt만으로B0를추정하지않는다. 복사한원본movie/native timestamps가보존되고target index와일치할때만B0를발행한다.

전체source/asset version·CFR·끝/PCM/최종clip 성공은보증하지않는다. source관계의초기관찰scope와target의complete frame/sample scope를구별한다. raw URL/header·metadata를공개하지않으며모든소유파일을registry에유지한다. 실패/취소에는Data=null이다.

Accurate는확인된source 시작부터요청OUT을포함하는whole prefix를수집한다. 요청이caller byte/span budget을넘으면명시거절하며전체파일fallback이나무제한확대는없다. HLS에서는audio가video segment끝보다앞설수있어기존후행reference 여유를유지한다. indexed에서는Accurate와audio에불필요한후행참조를더하지않고OUT을포함하는whole참조까지만선택한다. 이준비는decoder history/PCM qualification 성공과다르다.

자동698개통과(신규target guard7개), 실패/skip0. 실제samples/동일codec config·movie/track clock·index/native range·필요support·timestamp 변화를대조한다. 솔루션build 경고0/오류0. provider decoded size와known selected/auxiliary metadata dimensions의일치도검사한다.

실제 source service:

|실험|결과|소유파일|source offset/coverage|
|---|---|---:|---|
|YouTube137/140 Fast10~12초,32MiB/30초budget|Succeeded|10|V/A B0,[10,12)|
|CHZZK hls-8384 Accurate10~20초,32MiB/30초budget|Succeeded|3|V/A B0,[115187/9600,211187/9600)|
|YouTube137/140 Accurate10~20초,동일budget|Succeeded|10|V/A B0,[10,20)|

CHZZK origin19187/9600/FPS60/1,YouTube origin0/FPS30/1/초기관계Verified,모두NativeEnd Unknown을유지했다. source 처음부터의필요prefix와target 후보획득/매핑 검증이며최종10초clip을생성한결과가아니다. ignored `.artifacts/source-service-real/` GUID폴더의새소유파일과safe summaries를보존했다.

첫YouTube Accurate시도는불필요한audio 후행margin이40초whole-reference span을만들어30초상한에서UnsupportedFormat으로실패했다. 상한을조용히늘리지않고필요OUT까지만whole 참조를선택하도록수정한뒤성공했다. 실패의9개소유파일/NeedsCleanup도보존했다. package/tool 설치·cookies·전체영상·품질/모드fallback은없다.

다음은획득파일byte identity의후속local검사/처리인계·공유probe(4b)·qualification/처리(4c)와관련검증이다. 4a 최종완료audit·전체4단계완료는아직아니다. 단계별보고지시를유지하며개별commit보고를생략한다.
