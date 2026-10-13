# Phase 1 4a-3d-13 captured file byte identity handoff

2026-10-14, 시작 HEAD5cd62b4, phase/1, Base 유지.

후속local probe/처리가source/acquisition snapshot과같은내용을읽는지검사할수있도록Media.Core의MediaFileContentIdentity와OwnedMediaFile.Content를추가했다. positive bytes/64hexSHA256/lowercase/value equality를검사하고immutable하게보존한다. Content=null은실제bytes미확인상태이며파일경로/GUID만으로확정시키지않는다. 원격asset 전체version과구별한다.

HLS/indexed/standalone collector는완전수집/hash가끝난candidate에같은GUID/path/provenance와captured identity를인계한다. close/cancel실패의Data=null/잔존파일은기존소유정책을유지하며성공identity로승격하지않는다. 아직generic IMediaProbe/processing에서identity를검사하는구현은없다.

Core141개통과(새5개),Downloader698개통과/모두실패skip0. immutable normalized identity·size/hash차이·invalid 값과세collector의공개content/privatehash일치를검사했다. solutionbuild 경고0/오류0. 이번단위는새원격전송/clip생성을실행하지않았다.

다음4a종합audit/잔여negative lifecycle검증과4b공유probe·4cqualification/처리연결이남았다. 전체4단계/Phase1완료는아니다. 최신사용자지시인단계별보고·4단계완료후통합보고를유지한다.
