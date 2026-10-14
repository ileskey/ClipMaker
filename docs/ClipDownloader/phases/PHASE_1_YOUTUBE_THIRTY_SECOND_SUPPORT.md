# Phase 1 후속 — YouTube 10~40초 Fast 요청 지원 보완

2026-10-14. 시작 HEAD `d95d7b7`, branch `phase/1`에서 사용자 `wFOjWCLPDDM` / best137+140 / 25fps / Fast / IN10초·OUT40초의 indexed 수집 실패를 진단했다. 시작 시 미추적 사용자 파일 `22.mp4`를 발견해 조작하지 않았다. `out.mp4`도 조작하지 않고 고유 ignored 실험 경로만 썼다.

후속 [Accurate terminal PCM 지원](PHASE_1_ACCURATE_TERMINAL_PCM_SUPPORT.md)에서같은10~40초의Accurate실제생성·독립PCM 값대응을검증했다. 아래Accurate 미완료설명은이Fast 지원시점의이력이다.

## 확인한 두 차단 조건

현재 선택의 실제 SIDX를 각64KiB prefix로 읽었다. video는7초 단위7개 참조이며 `[0,1189/25)`=47.56초다. audio는약10초 단위5개이며 `[0,3568/75)`≈47.573333초다. 초기 참조 margin과 whole-reference 선택을 적용하면 video 요청 window `[3,47)`와 audio `[38/735,40)`이 양 스트림의 모든 참조와 겹친다. 원점0은 기존 실제 native 초기 관찰과 이번 통합 준비/실행의 재검증에서 확인했다. 기존40초 획득 상한으로는 video부터 실패한다.

선택 video 참조 payload 합계1,146,823 bytes, audio772,256 bytes로 byte 상한64MiB 문제가 아니다. init/prefix/초기/refresh/provider 비용은 이 합계와 별도로 누적된다. 이 짧은 입력은 계획된 whole references가 원본 media 전체 길이를 포함한다. 전체 길이를 포함하지 않는 구간만 받았다고 표현하지 않는다. collector의 명시 SIDX range 획득 전략을 유지하며 실패 후 whole-resource GET으로 우회하지 않았다.

64MiB/60초 제한 Application 실험은 시간 예산을 통과한 뒤 `NativeSkipUnsupported`에서 실패했다. final은 없고 owned 파일/잔존은0이었다. 예산만 늘린 성공으로 표시하지 않는다. 별도 고유 디렉터리에 같은 참조를 보존해 끝 음성을 실제 shared probe로 관찰했다.

audio sample/packet2051개·decoded frame2050개, leading skip1600은 기존 검증과 같았다. 마지막 native sample/packet은 rawPTS2099200 / moviePTS2097600 / duration384이다. 마지막 decoded frame도 PTS2097600 / duration384이지만 SampleCount1024다. 단일 rate1 edit의 표시 끝2097984와 마지막 유효 끝2097600+384가 같으며, decoder의 여분640 samples는 그 표시 끝 밖이다. 마지막 packet의 skip/discard side data는없다. 보존 파일773015 bytes·SHA256은 `74b3298fd26fd11c10305500154b1a0cdaaafae719bdba5e94b0f9e65ff43f90`다.

## 지원 계약과 변경

native gate는 AAC LC·정확한 clock/config/packet byte·단일 rate1 edit와 표시 끝을 그대로 검사한다. 마지막 sample만 duration1~1023이고 나머지는1024, PTS=DTS, 앞선 sample 끝이 마지막 시작 이후로 겹치지 않으며 마지막의 translated native 끝이 edit 끝과 정확히 같을 때만 terminal 대응을 허용한다. 모든 packet/frame의PTS/duration은 여전히 native와 정확히 같아야 한다. 마지막 decoded count는1024, 앞선 decoded count는 검증된 leading trim 이후 각 native duration과 정확히 같아야 한다.

여분을 `TrailingDecodedPaddingSamples`로 관찰 모델에 보존하고, 유효 ObservedRange는 native/frame duration의 표시 끝까지만 사용한다. 임의 tolerance·추정0·padding 값 생성·decoder 검사 생략은 없다. edit끝 불일치, 중간 short sample, 잘못된 frame count/duration, side-data discard padding은 계속 거절한다. 이 관찰을 remote 전체 동일성/SourceEndVerified로 승격하지 않는다.

Indexed 수집의 시간·개수·누적 bytes·단일 read cap·byte range·coverage 실패를 구체적인 field로 구별했다. `IndexedFetchedSpanBudget`는 whole span과 상한을 정확한 유리수로 안내한다. signed URL/headers는 메시지에 포함하지 않는다.

제한 실험의 생성 성공을 근거로 **Phase 1 현재 CLI 기본 예산을64MiB/60초**로 조정했다. 최종 제품 상한이나 모든30초 구간 지원 보장은 아니다. codec/sample/native/출력·확인·취소·정리·덮어쓰기 정책과 단일 read/JSON 상한은 유지한다. 이번 새 terminal 대응의 실제 생성 검증은 **Fast**이며 Accurate source-start PCM 준비의 terminal duration/count 지원은 완료로 표시하지 않는다.

## 실제 결과와 검증

보완 후 Application 실험 job `f98742f8-28f1-46ac-9afe-dc0f3846e903`은 최종 확정에 성공했다. `.artifacts/youtube-thirty-budget/fde31ef9f53f4e23a2f87dc8398e7ae3/clip.mp4`, SHA256 `5cf5b04cbfef91f86e965215ca6be0cc7b74f2292da6cc261f55707b7d07bb6f`다. 별도 전체 A/V decode도exit0이었다. 초기 지원 실패/보존 진단을 이 성공으로 세지 않는다.

요청은 `[10,40)`, 실제 Fast video는 `[7,1001/25)`=[7,40.04), audio는 `[7,17648/441)`≈[7,40.018141)다. output AAC skip2076/tail0, zero 삽입·중복 제거0이며 source 끝 여분640 samples는 이 출력 범위 밖이다. SourceEndVerified=false와 실제 시작/끝 차이를 안내한다.

솔루션 build 경고/오류0, Core143 / Media Infrastructure166 / Downloader851개, 총1160개 실패/skip0을 확인했다. 최초 native 테스트의 uint/ulong 상수 타입 오류2개를 수정한 후 재빌드·검증했다. 부정 테스트와 기존 시작 보정/실패/취소 회귀를 유지했다.

최종 기본값의 실제 CLI도같은 입력·best/Fast·`--yes`로 exit0/final 확정에 성공했다. job `2f004ee2-aaf3-4e76-9358-ae15db116686`, 출력 `.artifacts/youtube-thirty-cli/5dfdacaacddf4f3797ff018ebc3e7951/fast.mp4`, 1,365,856 bytes이며 SHA256과 실제 표시 범위가 Application 실험과 동일하다. 이전 전체 decode를 통과한 출력과 byte identity도같다. 실패/성공 실험과 실제CLI의 알려진 job 접두사 폴더는모두0, CLI staging도0을 확인했다. 보존한 별도 진단 참조는 cleanup 잔존과 구별한다.

영속 관찰은 [JSON 증거](evidence/PHASE_1_YOUTUBE_THIRTY_SECOND_SUPPORT.json)를 따른다. 직접 player 영상/음성·동기와 Phase1 전체 Passed/Completion/Tag는 Pending이며 Phase2/WPF 다운로드 UI/ClipEditor로 진입하지 않는다.
