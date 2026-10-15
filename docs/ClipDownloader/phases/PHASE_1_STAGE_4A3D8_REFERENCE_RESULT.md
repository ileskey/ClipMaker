# Phase 1 4a-3d-8 supplemental provider reference resolution

2026-10-14, 시작 HEAD8a5223a, phase/1, Base 유지.

동일 metadata 재조회에서 명시 선택137/140에 한해 보조 HLS270/234 locator를 private readonly collection으로 보존한다. 보조 주소는 선택을 대체하지 않으며 공개 snapshot에 포함되지 않는다. absent/unsafe locator는 관계 검사 후보에서 제외하며 selected pair는 그대로 유지된다. 첫 bridge 전략의 제한이고 범용 format 재선택이 아니다.

HlsPresentationReferenceResolver는 같은 master·고유 후보·역할 조건을 검사한 뒤512KiB 이하 complete master body를 읽고 group 선언을 해석한다. signed media 주소의 literal equality로 rendition 내용 동등성을 판정하지 않는다. master에 선언된 참조 URI를 같은 allowlisted header와 함께 다음 검사로 넘긴다. used bytes를 이전 source 수집 예산에 더한다. 실패/취소/Data=null·budget 거절·자동 fallback 금지를 유지한다.

629개 Downloader tests 통과(신규10개), 실패/skip0. supplemental 선택 불변/누락/unsafe, 동일master/중복/누락/byte budget/취소/명시Unknown 후보 처리 및 group URI/예산 인계를 확인했다.

실제 현재 metadata의270 video=Present/Absent,234 video=Absent/audio=Unknown, 같은 master를 확인했다. 처음에는 auxiliary audio Present 필수 gate로 조회 전에 거절됐다. audio Unknown을 **조사 후보**로 유지하고 absent를 거절하는 수정 뒤 actual master parse Succeeded/group234/body23445bytes였다. audio Unknown을 확인된 presence로 바꾸지 않았고 뒤 actual packed-frame/native gate가 판정해야 한다. 새 미디어 전송은 없고 master 선언만 읽었다. raw URI/header/metadata는 메모리에서만 다뤘다.

선언만으로 SharedAxis를 발행하지 않는다. supplemental leaf/packet payload·native timestamps·movie edit bridge 생산 경로, target/public 통합 및 전체4a/4단계가 남았다. 단계/Phase 완료를 표시하지 않는다.
