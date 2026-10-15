# Phase 1 4a-3d-7 bounded provider presentation declaration

2026-10-14, 시작 HEADf8273b7, phase/1, Base 유지.

bounded HLS master의 선택 보조 rendition에 대한 선언 parser를 구현했다. master512KiB/4096lines/line16KiB·strict UTF8, attribute 중복/quoted commas/URI bounds를 검사한다. 단일 video representation의 AUDIO group과 단일 audio representation의 GROUP-ID가 일치해야 한다. representation은 명시 itag path/query만 사용하며 duplicate를 거절한다. 상대URI를 master에 해석하고 safe HTTPS 조건을 유지한다.

성공은 provider가 준 grouping declaration이다. 원본137/140 content/native 대응이나 decoder 준비·SharedAxis Verified의 증거를 이 reader만으로 생산하지 않는다. 보조 참조 선택/HTTP/packet content 연결은 후속 단위다. URI는 ephemeral 내부 class에만 두고 record ToString이나 공개 데이터에 넣지 않는다.

자동 검증619개 통과(새11개), 실패/skip0. 단일 group/quoted CODECS와 mismatch·중복variant/attribute/representation·unsafe URI·누락rendition·broken quote/trailingcomma·invalid UTF8/상한 거절을 확인했다. 이번 단위는 synthetic declaration 검증이며 새 원격 master parse를 실행하지 않았다. 기존 G4b 실험의 grouping 결과를 새 reader의 actual 성공으로 대신하지 않는다. 전체4a/4단계는 진행 중이다.
