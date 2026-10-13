# Phase 1 4a-3d-11 production initial presentation relationship

2026-10-14, 시작 HEADbd5778b, phase/1, Base 유지.

local carrier probe/decode와 decoded picture parser/verifier, provider/payload/native/movie 관계의 초기 producer를 public indexed inspection에 연결했다. local bytes를 read-share lock과 pre/post hash로 보호하며 version15초/local inspection60초·complete stdout/root/descendant/drain 및 error stderr 거절을 적용한다. termination 미확인을 cancellation으로 숨기지 않는다.

decoded framehash 관찰은1MiB/4096picture 상한, single rawvideo/yuv420p 크기·exact clock/PTS/duration/sha를 검사한다. carrier의 actual pixel format/dimensions를 별도로 보존한다. source native frame과 decoded source 값/시간, decoded provider picture의 값/duration과 명시 movie edit 관계가 모두 맞아야 초기 관계 producer가 성공한다. 단순 시작값이나 encoded hash 하나만으로 성공시키지 않는다.

producer는 same selection·고유 source file provenance·index/native 범위 일치를 검사하고 현재 master→보조 whole first references→carrier packet/packed AAC→원본 payload/native 관계→decoded video 대조를 순서대로 실행한다. 생성한 보조 파일을 probe 전에 registry에 등록한다. source inspection public Files는 선택137/140 두 파일이며 소유 resource registry는 보조270/234까지 모두 인계한다. 실제 총 소비 budget을 source context에 보존한다.

최종691개 Downloader tests 통과(새24개), 실패/skip0, 솔루션 build 경고0/오류0. decoded rows의 clock/size/duplicate/stream/hash/order, decoder stderr/timeout/exit/incomplete output/termination/file identity, decoded 값/time/duration/representation/native 대조 실패를 검사했다.

보존된 source와보조video를actual ProcessRunner경로로각180pictures/1920x1080/source clock1/15360/carrier clock1/90000으로관찰했고picture verifier도180개로성공했다. 이후승인된source를새조회/선택검증한public production inspection에서다음결과를확인했다.

|관찰|결과|
|---|---|
|선택|137/140 그대로|
|inspection|Succeeded|
|owned resources|4 (selected2+supplemental2)|
|FPS|30/1|
|Origin|Verified 0/1|
|SharedAxis|Verified (초기 관찰 scope)|
|NativeEnd|Unknown|
|target Acquire|UnsupportedFormat/IndexedTargetStrategyUnavailable/Data=null|
|실패 후 소유/정리|4 files retained/NeedsCleanup|

공통 축·원점은초기provider/content/decoded/native/movie gate에기반한fact다. 전체source/end/CFR/decoder history/PCM·target/최종clip 성공을뜻하지않는다. indexed target은현재명시미지원gate에서차단하므로HLS전략으로오용하거나fallback하지않는다. 다음은indexed target 참조·관계 재검증 및scope/coverage mapping 연결이다. 전체4a/4b/4c 및4단계완료는아니다. 기존검증결과를Core/Media.Infrastructure전체테스트실행으로합산하지않는다.
