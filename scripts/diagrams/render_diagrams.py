"""Render source-verified architecture diagrams to PNG and editable SVG."""
from pathlib import Path
from html import escape
import math
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parents[2] / 'docs' / 'assets' / 'diagrams' / '2026-11-17'
OUT.mkdir(parents=True, exist_ok=True)
SCALE = 1.5
W, H = 2400, 1640
INK = '#15283D'
MUTED = '#51647A'
LINE = '#D8E1EA'
BLUE = '#2563EB'
TEAL = '#007F78'
PURPLE = '#7355C7'
AMBER = '#A96B16'
FONT = 'C:/Windows/Fonts/malgun.ttf'
BOLD = 'C:/Windows/Fonts/malgunbd.ttf'


class Canvas:
    def __init__(self):
        self.im = Image.new('RGB', (int(W*SCALE), int(H*SCALE)), '#F8FAFC')
        self.d = ImageDraw.Draw(self.im)
        self.svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">',
                    '<rect width="100%" height="100%" fill="#F8FAFC"/>']
        self.text_checks = []

    def box(self, x, y, w, h, fill='white', stroke=LINE, r=18, width=2):
        self.d.rounded_rectangle(tuple(int(v*SCALE) for v in (x,y,x+w,y+h)), radius=int(r*SCALE), fill=fill, outline=stroke, width=int(width*SCALE))
        self.svg.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{r}" fill="{fill}" stroke="{stroke}" stroke-width="{width}"/>')

    def text(self, x, y, s, size=28, color=INK, bold=False, anchor='left', max_width=None):
        f = ImageFont.truetype(BOLD if bold else FONT, int(size*SCALE))
        actual_w = self.d.textlength(s, font=f)/SCALE
        if max_width is not None:
            self.text_checks.append((s, round(actual_w, 1), max_width))
            if actual_w > max_width:
                raise ValueError(f'Text overflow: {s} ({actual_w} > {max_width})')
        px = x if anchor == 'left' else x-actual_w/2
        self.d.text((px*SCALE,y*SCALE), s, font=f, fill=color, anchor='lt')
        a = 'middle' if anchor == 'center' else 'start'
        self.svg.append(f'<text x="{x}" y="{y}" font-family="Malgun Gothic, sans-serif" font-size="{size}" font-weight="{700 if bold else 400}" fill="{color}" text-anchor="{a}" dominant-baseline="text-before-edge">{escape(s)}</text>')

    def lines(self, x, y, ss, size=26, color=MUTED, gap=40, max_width=None):
        for i,s in enumerate(ss):
            self.text(x,y+i*gap,s,size,color,max_width=max_width)

    def path(self, pts, color=MUTED, width=3, dashed=False, end=True, start=False):
        points=[(x*SCALE,y*SCALE) for x,y in pts]
        if dashed:
            for (x1,y1),(x2,y2) in zip(points,points[1:]):
                ln=math.hypot(x2-x1,y2-y1)
                for p in range(0,int(ln),int(15*SCALE)):
                    a=p/max(ln,1); b=min((p+8*SCALE)/max(ln,1),1)
                    self.d.line((x1+(x2-x1)*a,y1+(y2-y1)*a,x1+(x2-x1)*b,y1+(y2-y1)*b),fill=color,width=int(width*SCALE))
        else:
            self.d.line(points,fill=color,width=int(width*SCALE),joint='curve')
        st=' stroke-dasharray="8 7"' if dashed else ''
        self.svg.append(f'<polyline points="{" ".join(f"{x},{y}" for x,y in pts)}" fill="none" stroke="{color}" stroke-width="{width}" stroke-linejoin="round"{st}/>')
        for yes,tail,tip in ((end,pts[-2],pts[-1]),(start,pts[1],pts[0])):
            if not yes: continue
            ang=math.atan2(tip[1]-tail[1],tip[0]-tail[0]); length=13; half=6
            tri=[tip,(tip[0]-length*math.cos(ang)+half*math.sin(ang),tip[1]-length*math.sin(ang)-half*math.cos(ang)),(tip[0]-length*math.cos(ang)-half*math.sin(ang),tip[1]-length*math.sin(ang)+half*math.cos(ang))]
            self.d.polygon([(a*SCALE,b*SCALE) for a,b in tri],fill=color)
            self.svg.append(f'<polygon points="{" ".join(f"{a},{b}" for a,b in tri)}" fill="{color}"/>')

    def pill(self,x,y,w,s,color=BLUE,fill='#EFF5FF',size=22):
        self.box(x,y,w,40,fill,fill,r=20)
        self.text(x+w/2,y+6,s,size,color,bold=True,anchor='center',max_width=w-18)

    def title(self,number,title,subtitle):
        self.text(80,40,'BANNERGUARD SYSTEM  /  TECHNICAL OVERVIEW',22,BLUE,True)
        self.text(80,85,title,58,INK,True)
        self.text(80,163,subtitle,27,MUTED,max_width=2200)
        self.pill(2160,76,160,number,color=INK,fill='#E9EEF5')

    def card(self,x,y,w,h,title,body,color=BLUE,fill='white',title_size=30,size=25):
        self.box(x,y,w,h,fill)
        self.box(x+18,y+22,6,h-44,color,color,r=3)
        self.text(x+42,y+22,title,title_size,color,True,max_width=w-65)
        self.lines(x+42,y+70,body,size,gap=37,max_width=w-65)

    def footer(self,s):
        self.path([(80,1565),(2320,1565)],LINE,2,end=False)
        self.text(80,1590,s,22,MUTED,max_width=1950)
        self.text(2320,1590,'2026.11.17',22,MUTED,anchor='center')

    def save(self,name):
        self.svg.append('</svg>')
        (OUT/f'{name}.svg').write_text('\n'.join(self.svg),encoding='utf-8')
        self.im.save(OUT/f'{name}.png',dpi=(240,240),optimize=True)
        self.im.resize((1800,1230),Image.Resampling.LANCZOS).save(OUT/f'{name}_preview.png')
        print(f'{name}: {self.im.size}, {len(self.text_checks)} text-width checks passed')


def architecture():
    c=Canvas()
    c.title('01 / 02','시스템 아키텍처','현재 구현 기준 · WPF 클라이언트, FastAPI 서버, GPU 추론 및 데이터 저장 흐름')
    # Each large enclosure denotes a process, not a separate computer.
    c.box(80,325,760,800,'#EDF4FF','#C3D6F5',r=24)
    c.box(1490,325,830,800,'#EBF6F3','#BDDED5',r=24)
    c.text(112,351,'WPF 클라이언트',36,BLUE,True)
    c.text(112,403,'C# · .NET 9 · 관제 화면 프로세스',25,MUTED)
    c.text(1522,351,'Python AI 서버',36,TEAL,True)
    c.text(1522,403,'FastAPI · Uvicorn · 포트 8000',25,MUTED)

    # Lifecycle, kept outside the request/result lanes.
    c.path([(460,325),(460,267),(1905,267),(1905,325)],PURPLE,3,True)
    c.box(715,237,945,57,'#F8FAFC','#F8FAFC',r=8)
    c.text(1187,246,'BackendManager · 서버 재사용 또는 자식 프로세스 실행·종료',25,PURPLE,True,anchor='center',max_width=920)

    c.card(110,460,700,142,'관제 화면',[
        'CCTV 모니터링 · VLM 검증 · 단속 처리',
        '통계/리포트 · 데이터 관리'],BLUE)
    c.card(110,625,700,130,'영상 재생 및 오버레이',[
        'LibVLCSharp · MP4 재생 / YOLO 박스 표시'],BLUE)
    c.card(110,778,700,175,'통신 및 화면 상태 서비스',[
        'WebSocketService → AlertService → ViewModel',
        'SystemMonitorService · /health 주기 조회'],BLUE)
    c.card(110,976,700,118,'데이터 및 보고서',[
        'DatabaseService · PDF · Excel'],BLUE,title_size=29,size=25)

    c.card(1520,460,770,142,'API / WebSocket 라우터',[
        '/ws/alerts · 결과 전송 및 명령 수신',
        '/health · 모델 준비 여부 및 하드웨어 상태'],TEAL)
    c.card(1520,625,770,218,'AIInferenceService',[
        'CCTV: YOLO26s → 현수막 박스·신뢰도',
        'VLM: YOLO26s → 크롭 → Qwen2.5-VL-3B',
        '문자·맥락 분석 → 상태코드 / 판정 결과'],TEAL)
    c.card(1520,866,770,118,'추론 실행 환경',[
        'PyTorch CUDA · VLM 4-bit / CUDA 없으면 CPU'],TEAL,title_size=29,size=25)
    c.card(1520,1007,770,87,'상태 수집: psutil · NVML',[],TEAL,title_size=28)

    # Three independent transport lanes.
    c.pill(953,351,424,'동일 PC · localhost 통신',INK,'#E9EEF5')
    c.text(1165,438,'분석 요청 / 관제 명령',27,BLUE,True,anchor='center')
    c.text(1165,481,'cctv_analyze · toggle_automation',22,MUTED,anchor='center')
    c.text(1165,515,'human_verdict',22,MUTED,anchor='center')
    c.path([(840,569),(1490,569)],BLUE,4)
    c.text(1165,665,'분석 결과 · JSON + 이미지',27,TEAL,True,anchor='center')
    c.text(1165,709,'channel_batch / vlm_image_batch',23,MUTED,anchor='center')
    c.path([(1490,766),(840,766)],TEAL,4)
    c.text(1165,865,'HTTP GET /health',27,PURPLE,True,anchor='center')
    c.text(1165,908,'AI 준비 상태 · GPU / VRAM · 서버 자원',22,MUTED,anchor='center')
    c.path([(840,958),(1490,958)],PURPLE,3,True,start=True)
    c.text(1165,1040,'WebSocket: ws://localhost:8000/ws/alerts',21,MUTED,anchor='center')

    # Local sources: the same video files are played and decoded separately.
    c.path([(460,1195),(460,1125)],BLUE,3)
    c.text(465,1146,' 영상 재생',22,BLUE)
    c.card(80,1195,760,225,'로컬 영상 · 설정된 미디어 폴더',[
        'MP4 4채널 · 관제 화면에서 반복 재생',
        '재생 위치(ms)를 서버 분석 요청에 포함',
        '현재 구성은 로컬 영상 기반 데모'],BLUE,fill='white',title_size=30,size=25)
    c.path([(1905,1195),(1905,1125)],TEAL,3)
    c.text(1910,1146,' 프레임·이미지 로드',22,TEAL)
    c.card(1490,1195,830,225,'서버 분석 입력',[
        'OpenCV → 같은 MP4의 지정 시각 프레임 추출',
        '설정된 VLM 이미지 폴더 → 원본 입력',
        'CCTV: 화면 진입 후 주기 요청 / VLM: 1회 배치'],TEAL,title_size=30,size=25)

    # Shared file persistence, both processes explicitly linked.
    c.box(900,1195,530,225,'#FFF7E9','#E8D4AF',r=22)
    c.text(1165,1220,'공유 SQLite DB',32,AMBER,True,anchor='center')
    c.text(1165,1274,'banner_tracking.db',27,INK,True,anchor='center')
    c.lines(931,1322,['탐지 이력 · 사람 검토 · 단속 기록','WPF와 Python이 각각 읽기·쓰기'],23,gap=37,max_width=470)
    c.path([(840,1050),(870,1050),(870,1308),(900,1308)],AMBER,3,start=True)
    c.path([(1490,1050),(1460,1050),(1460,1308),(1430,1308)],AMBER,3,start=True)

    c.box(80,1465,2240,65,'#E9EEF5','#E9EEF5',r=14)
    c.text(1200,1481,'가중치 실물 확인: best.pt  /  yolo26s.yaml · scale=s · banners 1클래스  |  AI 연산은 Python 서버에서 수행',25,INK,anchor='center',max_width=2160)
    c.footer('실선 = 명령·결과·파일 접근   ·   점선 = 생명주기 제어 / 상태 조회   ·   현재 소스 구조를 도식화')
    c.save('system_architecture_20261117')


def mvvm():
    c=Canvas()
    c.title('02 / 02','MVVM 다이어그램','실제 클래스 기준 · View는 표시, ViewModel은 상태·명령, Service는 통신·저장, Model은 데이터')
    c.box(80,236,2240,108,'#15283D','#15283D',r=18)
    c.text(113,255,'App.OnStartup → AppBootstrapper.Build()',30,'white',True)
    c.text(113,302,'DI 구성 · Shell/CCTV/VLM ViewModel 및 핵심 서비스는 Singleton · 단속/통계/설정 ViewModel은 Transient',24,'#C9D8E8',max_width=2160)

    vx,vw,mx,mw,sx,sw=80,540,760,620,1530,790
    for x,w,label,col,bg in [(vx,vw,'VIEW  ·  XAML / 화면',BLUE,'#EDF4FF'),(mx,mw,'VIEWMODEL  ·  상태와 명령',TEAL,'#EBF6F3'),(sx,sw,'SERVICE  ·  외부 작업',PURPLE,'#F2EEFA')]:
        c.box(x,374,w,61,bg,bg,r=14)
        c.text(x+24,388,label,29,col,True,max_width=w-48)

    rows=[
        (462,135,'앱 셸 / 화면 전환',['MainWindow.xaml'],'ShellViewModel', ['CurrentView · 상태바 · 탐색 명령'], ['INavigationService → NavigationService','SystemMonitorService'], ['화면 캐시·탐색 / 연결 및 자원 상태']),
        (622,135,'CCTV 모니터링',['CctvMonitoringView.xaml'],'CctvMonitoringViewModel', ['Channels · SelectedChannel · 분석 요청'], ['WebSocketService · AlertService','SystemMonitorService'], ['channel_batch → 채널 및 탐지 로그']),
        (782,135,'VLM 검증',['RealTimeMonitoringView.xaml'],'RealTimeMonitoringViewModel', ['ActiveAlert · Feed 필터 · 사람 판정'], ['AlertService · WebSocketService','DatabaseService'], ['vlm_image_batch → Feed / 판정 저장']),
        (942,135,'단속 처리 현황',['EnforcementStatusView.xaml'],'EnforcementStatusViewModel', ['Records · 검색 · 접수/검토 · 보고서'], ['DatabaseService · PdfReportService','IDialogService → WpfDialogService'], ['단속 목록·처리 상태 / PDF·Excel']),
        (1102,158,'통계/리포트 · 데이터 관리',['StatisticsReportView.xaml','SystemSettingsView.xaml'],'StatisticsReportViewModel', ['SystemSettingsViewModel'], ['IStatisticsRepository · ISettingsRepository','IStatisticsExcelExportService · IDialogService'], ['SQLite 통계 / JSON 설정 / Excel 출력']),
    ]
    for y,h,title,views,vm,vm_sub,svc,svc_sub in rows:
        c.box(vx,y,vw,h,'white',LINE)
        c.text(vx+25,y+17,title,29,BLUE,True,max_width=vw-50)
        c.lines(vx+25,y+63,views,25,gap=34,max_width=vw-50)
        c.box(mx,y,mw,h,'white',LINE)
        c.text(mx+25,y+25,vm,27,TEAL,True,max_width=mw-50)
        c.lines(mx+25,y+78,vm_sub,24,gap=34,max_width=mw-50)
        c.box(sx,y,sw,h,'white',LINE)
        c.lines(sx+26,y+17,svc,26,INK,gap=37,max_width=sw-52)
        c.lines(sx+26,y+99,svc_sub,23,MUTED,max_width=sw-52)
        # UI -> ICommand and ViewModel -> binding updates are separate directions.
        c.path([(vx+vw,y+50),(mx,y+50)],BLUE,3)
        c.path([(mx,y+97),(vx+vw,y+97)],TEAL,3,True)
        c.text((vx+vw+mx)/2,y+21,'Command',18,BLUE,anchor='center')
        c.text((vx+vw+mx)/2,y+105,'Binding',18,TEAL,anchor='center')
        c.path([(mx+mw,y+50),(sx,y+50)],PURPLE,3)
        c.text((mx+mw+sx)/2,y+21,'호출 / 구독',19,PURPLE,anchor='center')
        c.path([(sx,y+97),(mx+mw,y+97)],PURPLE,3,True)
        c.text((mx+mw+sx)/2,y+105,'결과 / 이벤트',18,PURPLE,anchor='center')

    # Models are shown separately from services, as shared data contracts.
    c.box(80,1311,2240,177,'#FFF7E9','#E8D4AF',r=20)
    c.text(110,1332,'MODEL / DTO  ·  ViewModel과 Service가 사용하는 데이터 타입',29,AMBER,True,max_width=2150)
    c.text(110,1386,'CCTV: CctvChannel · ChannelDetectionBatchDto     |     VLM: BannerDetectionEventDto',26,INK,max_width=2150)
    c.text(110,1431,'단속: EnforcementRecord · SimilarEnforcementRecord     |     통계/설정: StatisticsReportData · SystemSettingsRoot',25,INK,max_width=2150)

    c.text(80,1511,'변경 전달: INotifyPropertyChanged · ObservableCollection   |   UI 명령: RelayCommand / SimpleCommand   |   오버레이: DependencyProperty',23,MUTED,max_width=2240)
    c.footer('CctvChannelControl · YoloBBoxOverlay는 View 구성요소   ·   BackendManager는 App 시작/종료에서 호출   ·   서비스는 행별로 중복 표기')
    c.save('mvvm_diagram_20261117')


if __name__ == '__main__':
    architecture()
    mvvm()
