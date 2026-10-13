# MEP Export BOM — Revit 2023

Add-in C# cho **Revit 2023**, `.NET Framework 4.8`, x64. Chạy bằng **Add-In Manager (Manual)**.
Command: `MEP_Export_BOM_From_View_ver1.ExportBomCommand`.
Preview/Excel không lưu thay đổi vào model. Preview schedule theo phạm vi dùng transaction tạm rồi rollback. **Create Revit Schedules** tạo ViewSchedule; khi bật **Match BOM scope (host)**, thao tác còn tạo/bổ sung parameter chỉ đọc `MEP BOM Scope Membership` (cột bị ẩn trong schedule, hiện trong Properties khi có giá trị) và đánh dấu các element host thuộc phạm vi. Cả batch là một thao tác Undo. Model link không bị sửa.

## Chạy test bằng Add-In Manager

1. Mở project model trong Revit 2023.
2. Vào **Add-Ins → Add-In Manager (Manual)**. Tên/vị trí menu có thể khác tùy bản Add-In Manager bạn cài.
3. Chọn **Load** và mở `bin\Debug\MEP_Export_BOM_From_View_ver1.dll` trong thư mục project này.
4. Chọn `MEP_Export_BOM_From_View_ver1.ExportBomCommand` → **Run**.
5. Chọn phạm vi: **Entire Model / 3D View / Floor Plan / Scope Box**.
6. Chọn view hoặc Scope Box. Entire Model không cần đối tượng này.
7. Khi mới mở hoặc đổi phạm vi/view, danh sách category và bảng BOM trống. Bấm **Preview BOM** để quét phạm vi và hiện category có element; lần đầu chọn nhóm MEP mặc định, nếu không có MEP thì chọn toàn bộ nhóm có trong vùng.
8. Tick/bỏ tick category để cập nhật bảng ngay; không phải bấm Preview lại. **MEP / All / None** chọn/bỏ các nhóm có dữ liệu. Category không có trong phạm vi vẫn hiện cuối danh sách, màu nhạt và disabled; tooltip ghi rõ không có dữ liệu. Chỉ cần Preview lần đầu hoặc sau khi đổi phạm vi/view/Scope Box. Kiểm tra **Notes**; **Modify Title** ở thanh nút dưới cùng chỉ tác động tab hiện tại.
9. Mở tab **Excel / CSV**. Để trống Template để xuất form mặc định; hoặc chọn template `.xlsx`, tên sheet và dòng bắt đầu ghi. Có thể import file để lọc element theo một cột khóa.
10. Bấm **Export Excel**, chọn file mới và mở bằng Excel để kiểm tra.

Sau khi thay code, build lại và load/reload DLL trong Add-In Manager. Nếu DLL bị khóa, đóng cửa sổ tool; có thể cần khởi động lại Revit. Không cần cài manifest `.addin` riêng để chạy command qua Add-In Manager đã cài.

## Quy tắc thống kê

| Phạm vi | Cách lấy dữ liệu |
|---|---|
| Entire Model | Instance trong document hiện tại, theo category được chọn |
| 3D View | Collector theo view; lọc thêm bounding box nếu Section Box đang bật |
| Floor Plan | Collector theo view; kiểm tra bounding box với Crop Region nếu đang bật |
| Scope Box | Kiểm tra giao giữa bounding box element và vùng Scope Box, hỗ trợ Scope Box xoay |

- Mặc định thống kê host. Bật **Include Revit Links** để lấy thêm các link trực tiếp đã load; có cột Source và audit Link Instance ID/Unique ID. Link unload được ghi Notes. Chưa lấy nested link.
- Chọn một view hoặc một Scope Box cho mỗi lần xuất.
- Categories và BOM dùng cùng collector theo view, crop/section boundary và loại element/category bị ẩn. Không loại element chỉ vì không đọc được hình học hiển thị. Category có instance trong phạm vi được bật; các model category còn lại bị disabled. Đây không phải kiểm tra từng pixel; đối tượng bị che bởi đối tượng khác vẫn có thể được lấy. Entire Model và Scope Box dùng phạm vi model/vùng.
- Lọc vùng dựa trên bounding box, có thể lấy dư nếu box chứa khoảng trống. Element không có bounding box sẽ không được lấy khi cần lọc vùng.
- **Element giao biên được lấy toàn bộ khối lượng.** Ống dài 10 m, chỉ 3 m nằm trong Scope Box, vẫn thống kê 10 m.
- 3D perspective và floor plan có split crop chưa hỗ trợ. Dùng 3D orthographic và crop không split.
- `MEPCurve` (Pipe, Flex Pipe, Duct, Flex Duct, Cable Tray, Conduit...) cộng chiều dài, đơn vị `m`.
- Mặc định category khác đếm instance (`pcs`). **Tools → Measurement rules** đổi từng category sang Length (`m`), Area (`m²`) hoặc Volume (`m³`) khi element có built-in quantity phù hợp. Quantity không đọc được sẽ là 0 và được đánh dấu/cảnh báo. Quy tắc này áp dụng BOM/Excel; field và units native schedule cấu hình riêng.
- Gom nhóm theo nguồn model/link instance, category ID, type ID, family/type, system, kích thước chính xác và đơn vị. ID trùng giữa host/link không bị gộp nhầm. Làm tròn hiển thị không làm gộp kích thước khác nhau.
- Kích thước lấy từ built-in parameters; fitting/equipment có thể lấy từ connector. Kích thước hiển thị bằng mm.
- System lấy từ parameter hoặc connector. Instance không có thông tin system để trống.
- Element đọc lỗi được ghi trong Notes; cần kiểm tra Notes trước khi dùng BOM.

## Form Excel mặc định

### Các tab BOM, Schedule và Excel / CSV

- **BOM Preview**: bảng khối lượng gom nhóm theo phạm vi và category được chọn; dùng cho xuất Excel.
- **Revit Schedule Preview**: chọn một category được hỗ trợ trong các category đã chọn. Tool tạo schedule tạm theo cấu hình riêng của category, đọc bảng native của Revit rồi rollback toàn bộ schedule tạm. Cả hai bảng dùng style của dự án, có resize cột và tooltip cho nội dung dài. Không lưu schedule khi chỉ xem trước.
- **Modify Title** ở thanh nút dưới cùng: tại BOM đổi title/cột Excel; tại Schedule đổi title, field native, heading, thứ tự, ẩn field và Calculate totals cho category đang xem. Field ẩn vẫn dùng được trong filter/sort. Nút đổi tên theo tab và không xuất hiện ở tab Excel / CSV.
- **Create Revit Schedules** cũng nằm dưới cùng, chỉ hiện/bật tại tab Schedule khi preview hợp lệ. Batch dùng bản sao cố định của cấu hình từng category đã chọn, gồm field/heading/unit/totals/filter/sort/group và danh sách host IDs; kiểm tra preview của từng cấu hình trước khi cho nhập tên và tạo. Chỉ tên schedule được đổi trong dialog đặt tên.
- Tab **Excel / CSV** chứa toàn bộ mục 04 trước đây: template, sheet, dòng data, tạo sheet riêng theo category, column mapping và import selection. Export BOM Excel có ở tab BOM/Excel; Export Schedules có ở tab Schedule.
- **Select / Export Schedules**: chọn nhiều category schedule bằng checkbox (All/None), xuất thành nhiều worksheet trong một `.xlsx`. Mỗi sheet dùng title, field, filter và sorting/grouping riêng. Không dùng template Excel của BOM cho chức năng này.
- **Match BOM scope (host)** mặc định bật: schedule lấy chính các host instance còn lại trong BOM sau chọn category/filter, qua parameter membership chỉ đọc, cột ẩn và một filter Contains. Bỏ tick để dùng Entire host model. Phạm vi được ghi trên tab. Link không thuộc native schedule này; dùng BOM/Excel để thống kê link. Membership của schedule đã tạo là snapshot; Refresh BOM không tự sửa các schedule đã tạo trước đó.
- Scope membership chiếm một filter native, nên khi bật Match scope chỉ dùng tối đa 7 user filter. Category không cho bind instance parameter, hoặc element/group/worksharing không cho ghi marker sẽ báo lỗi; batch không lưu một phần. Có thể dùng Entire host model cho những category đó.

### Tools và đối chiếu

- **Refresh** quét lại dữ liệu phạm vi khi model thay đổi; tick category tiếp tục dùng cache. Bật/tắt Include Links cũng yêu cầu quét lại.
- **Highlight / Close**: chọn một/nhiều dòng BOM, bấm để đóng tool, chọn các instance và zoom trong Revit. Hỗ trợ reference đến linked element; nếu view không cho zoom, selection vẫn được giữ và Revit báo lý do.
- **Tools → Save/Load preset** lưu title/cột, category, filter/sort/group, cấu hình native theo category, measurements, template mapping và tùy chọn links/scope vào `.preset.xml`. Có thể chọn vị trí lưu. Không tự mở file/template khi load. Field native được resolve lại theo built-in key hoặc tên/field type; field không thể resolve an toàn sẽ chặn load. Project-specific parameter cần model gốc; shared parameter dùng GUID để có thể dùng ở dự án khác.
- **Quality / warnings** mở Notes. Dòng thiếu System/Size (Pipe/Duct), quantity không hợp lệ hoặc giá trị `<varies>` được đánh màu và có cột Warnings/tooltip. Audit giữ Source, Link Instance ID và Unique ID.
- **Save BOM snapshot** lưu `.bom.xml`. Mỗi lần Export Excel cũng lưu snapshot cùng tên cạnh workbook. **Compare with previous snapshot** hiển thị Added/Removed/Changed và delta; có Export changes. So sánh dùng source + UniqueId, yêu cầu cùng project, view/scope, category/filter/link và measurement settings. Đổi unit chỉ ghi thay đổi, không trừ hai đơn vị khác nhau.
- **Export existing Revit schedules** chọn nhiều schedule trong model để xuất mỗi schedule thành một sheet. Đây là đọc bảng hiện tại, không tạo/chỉnh schedule. Bảng dùng giá trị đã định dạng bởi Revit, bao gồm totals.
- **Template column mapping** gán field vào cột bất kỳ (ví dụ B/D/F/AA), giữ ô/công thức ở cột không map; Sheet và Data row dùng controls chính. Header ở ngay trên Data row. Total row tùy chọn nằm dưới dữ liệu, chỉ dùng khi có một đơn vị. Không ghi đè ô có dữ liệu/công thức hoặc vùng merge; template gốc được giữ.
- Revit Link theo view dùng bounding box đã biến đổi qua link transform, crop/section boundary và plan view range. Revit 2023 không có collector view/link giống phiên bản mới: linked-view/custom overrides, phase mapping và nested links không được tái tạo. Notes ghi rõ giới hạn; kiểm tra số liệu link trước khi sử dụng.

### Filter / Sorting & Grouping

- Cả hai tab có nút **Filter / Sorting & Grouping**, cấu hình độc lập. Tối đa 8 filter nối AND và 4 sort/group field theo thứ tự; có Move up/down. Click header đổi tăng/giảm, Shift-click thêm cấp sort. Các thao tác này tự cập nhật preview và áp dụng khi xuất/tạo schedule.
- Điều kiện: Equal/NotEqual, Greater/Less (và OrEqual), Contains/NotContains, BeginsWith/EndsWith (và phủ định), HasValue/HasNoValue, HasParameter và association với global parameter (nhập ID). BOM association dùng field parameter và lọc instance trước khi gom nhóm. Revit kiểm tra field/operator native có được hỗ trợ trước khi nhận cấu hình; cấu hình không hợp lệ báo lỗi và giữ cấu hình cũ.
- Native filter số dùng đơn vị project hoặc suffix đơn vị (ví dụ `50 mm`); filter ElementId/global parameter nhập ID. Có thể thêm field rồi ẩn nó để dùng filter/sort.
- Chọn rule rồi **Load values** để chọn giá trị thực tế từ danh sách. Native ElementId hiện `ID · tên` nhưng lưu giá trị ID; text và numeric value theo parameter/project units. Global-parameter association vẫn nhập global parameter ID.
- Sorting/Grouping có Ascending/Descending, Header, Footer, Footer title/count, Blank line, Itemize every instance, Grand totals, title/count và tên tổng. Native dùng trực tiếp ScheduleDefinition; BOM mô phỏng trên dữ liệu của phạm vi đã quét.
- BOM filter chạy trên các dòng itemized hoặc dòng gom nhóm cơ sở trước khi group theo sort field. Quantity dùng `m`/`pcs`; parameter bổ sung là chuỗi hiển thị. Khi bỏ Itemize và chọn sort field, BOM gom theo các field đó và đơn vị; giá trị khác nhau hiển thị `<varies>`. Tổng luôn tách riêng `m` và `pcs`.
- Dữ liệu phạm vi được cache trong lúc cửa sổ mở để tick category không quét lại Revit. Đổi phạm vi/view sẽ xóa cache và yêu cầu Preview mới; cấu hình từng tab/category được giữ trong phiên.

### Import Excel / CSV bằng một cột khóa

1. Preview phạm vi cần thống kê, rồi mở **Excel / CSV → Import Excel / CSV**.
2. Chọn `.xlsx` hoặc CSV UTF-8. Dialog đọc các sheet và gợi ý dòng header (BOM mặc định dòng 5). Chọn sheet/dòng header; nếu đổi dòng, bấm **Read headings**.
3. Chọn **Key column** và **Target**. Header trùng tên cột BOM (kể cả heading đã sửa), tên/key parameter hoặc heading native đã cấu hình được gợi ý tự động khi khớp không mơ hồ. Có thể chọn target thủ công. Chỉ dùng một cột khóa; các cột khác không tham gia điều kiện.
4. Bấm **Match / Preview**. Mỗi dòng có giá trị khóa không trống lấy tất cả element có giá trị tương ứng trong phạm vi đã quét. Hợp các dòng và loại ID trùng; bảng báo số match từng dòng và các dòng không khớp.
5. **Apply selection** lọc BOM về các ID khớp và chọn category tương ứng. BOM filter/measurement rules vẫn áp dụng tiếp. Kết quả không có match vẫn trống, không tự quay về toàn model. Native Schedule với Match BOM scope dùng chính các host ID còn lại; link không thuộc native schedule này.

- Đây là chọn/lọc dữ liệu, không ghi parameter của element từ file.
- Text so sánh không phân biệt hoa/thường, trim khoảng trắng. Mark dạng chuỗi `001` khác `1`; XLSX có number format đơn giản `000` được giữ padding. Các định dạng Excel phức tạp/date không được tái tạo đầy đủ; nên lưu khóa định danh dưới dạng Text.
- Parameter numeric so sánh số; giá trị không có suffix dùng đơn vị hiển thị của parameter. Có hỗ trợ suffix m/mm/cm/ft/in và m²/m³. Cột Size là chuỗi mô tả, không được coi là một số diameter; dùng parameter Diameter để khớp số.
- Có thể dùng Element ID/Unique ID; cột Element IDs chứa danh sách phân cách comma/semicolon sẽ khớp từng ID. ID số có thể trùng giữa host/link; kiểm tra matched count, ưu tiên Unique ID hoặc parameter đặc trưng trong phạm vi thích hợp.
- XLSX dùng inline/shared strings hoặc cached formula results; nếu công thức chưa có kết quả lưu, cần recalculate và save bằng Excel. Không chạy macro/công thức khi import. CSV hỗ trợ comma/semicolon/tab, quote/double quote và giá trị nhiều dòng.
- Giới hạn 20 MB/file, 20.000 dòng/sheet và 1.024 cột. Chưa hỗ trợ `.xls`.
- **Clear import filter** bỏ giới hạn ID và khôi phục category chọn trước import. Đổi phạm vi/view, Include Links hoặc Refresh xóa import filter để không giữ ID từ phạm vi cũ. Import selection chưa lưu trong preset; snapshot ghi fingerprint của tập ID để không so sánh nhầm điều kiện.
- Manual test: import danh sách Type/Mark/Diameter, kiểm tra element IDs với Properties; thử nhiều dòng trùng, giá trị không có trong model, text có số 0 đầu và link ID trùng; kiểm tra Schedule Preview và Schedule tạo với Match BOM scope có cùng member IDs/field/filter/group.

Sheet **BOM**, header dòng 5, dữ liệu từ dòng 6:

| Cột | Nội dung |
|---|---|
| A | STT |
| B | Category |
| C | Family |
| D | Type |
| E | System |
| F | Size |
| G | Unit — đơn vị |
| H | Quantity — số lượng/khối lượng |
| I | Element Count — số element |
| J | Element IDs |

Sheet **Elements**: một dòng mỗi element, ID, thông số, khối lượng và ghi chú để đối chiếu.
Sheet **Notes**: quy tắc lấy dữ liệu và cảnh báo; metadata gồm model, phạm vi và thời gian UTC+7.
Tùy chọn **Separate sheet per category** mặc định bật: thêm một sheet cho mỗi category có dữ liệu xuất, chỉ chứa BOM của category đó, STT bắt đầu lại từ 1. Tên sheet được làm sạch ký tự không hợp lệ, giới hạn 31 ký tự và thêm hậu tố khi trùng. Có thể bỏ chọn để chỉ xuất các sheet tổng hợp/audit.
Không cộng tổng chung chiều dài với số lượng vì khác đơn vị.
Quantity được lưu dạng số, giữ độ chính xác gốc; không phải chuỗi đã làm tròn.
Cell Element IDs quá dài được cắt theo giới hạn 32.767 ký tự của Excel; sheet Elements giữ từng ID riêng.

## Dùng template riêng

- Chỉ hỗ trợ `.xlsx` không mã hóa; không cần cài Excel trên máy để xuất.
- Mapping mặc định **A:J** như bảng trên; sau Modify Title, thứ tự/số cột theo danh sách field đang hiển thị. Ô Sheet trống dùng sheet đầu tiên, hoặc nhập tên sheet đích.
- **Data row** là dòng ghi dữ liệu đầu tiên; mặc định 6. Khi không chọn template, form mặc định luôn bắt đầu dòng 6.
- Vùng từ **cột A đến cột cuối được xuất, từ Data row trở xuống phải trống dữ liệu/công thức**. Có thể giữ format ô và chiều cao dòng; format được sao chép từ dòng Data row cho các dòng BOM mới.
- Nếu Data row > 1, dòng ngay phía trên là header: ghi tên các cột hiện tại, giữ style ô header; bỏ header dư trong vùng A:J mặc định khi ẩn cột. Các ô header A:J là vùng dành cho tool. Không dùng merged cells trong vùng header/data này. Title/logo nằm ngoài vùng này được giữ trong template; title mới áp dụng cho preview và các sheet BOM được tạo mới.
- Sheet dữ liệu không được protected hoặc có Excel Table. Merged cells không được giao vùng sẽ ghi dữ liệu.
- Nếu có dòng tổng/footer trong vùng cột được xuất, chuyển chúng sang vùng khác trước khi dùng template. Print area được giữ nguyên; cần chỉnh lại nếu số dòng xuất vượt vùng in đã định.
- Thêm các sheet **BOM_Elements / BOM_Notes** vào file kết quả; nếu trùng tên, thêm hậu tố.
- File template gốc được giữ nguyên. Không cho lưu kết quả đè lên template.

## Build

Mở `.csproj` hoặc `.slnx` bằng Visual Studio có .NET Framework 4.8 targeting pack.
Build Configuration **Debug** hoặc **Release**. RevitAPI/RevitAPIUI đặt `Copy Local = false`.
Mặc định tham chiếu `C:\Program Files\Autodesk\Revit 2023`.

PowerShell:

```powershell
.\build.ps1
.\build.ps1 -Configuration Release
.\build.ps1 -RevitInstallDir 'D:\Autodesk\Revit 2023'
```

Không cần restore NuGet: xuất XLSX bằng Open XML package với thư viện .NET có sẵn.

## Manual test trên model

Tạo hoặc dùng model nhỏ có 2 ống cùng type/system/diameter, 1 ống khác diameter, 2 fitting cùng type/size và 1 equipment. Đối chiếu chiều dài với Properties hoặc Revit Schedule cùng điều kiện.

| Test | Kết quả cần kiểm tra |
|---|---|
| Entire Model + Pipes | Các ống cùng type/system/size gộp; tổng chiều dài bằng tổng chiều dài element |
| Pipe Fittings | Đếm theo family/type/connector size; không dùng đơn vị m |
| Nhiều category | Dòng BOM tách category; chiều dài và số lượng không cộng chung |
| 3D có Section Box | Element ngoài box không được lấy; element giao biên lấy toàn bộ chiều dài |
| Floor Plan có crop | Element ngoài crop được loại theo bounding box; view range/phase/filter theo collector của Revit |
| Scope Box xoay | Element rõ ràng ngoài box bị loại; element trong/giao box được lấy |
| 2 element cùng type nhưng khác system/size | Không gộp chung |
| Không chọn category / không có target | Báo lỗi rõ, không xuất dữ liệu cũ |
| Thay phạm vi/category sau preview | Xóa preview và yêu cầu Preview lại trước khi Export |
| Export mặc định | Excel mở không repair; Unicode đúng; Quantity là số; có BOM/Elements/Notes |
| Template trống có style dòng 6 | Ghi A:J, giữ format/header; không thay template gốc |
| Template có dữ liệu/formula/merge trong vùng ghi | Báo lỗi; không ghi file kết quả dở dang |
| File đích đang mở hoặc thư mục không ghi được | Báo lỗi xuất; model không bị thay đổi |

## Kiểm tra tự động đã chạy

`Tests\SmokeTests.csproj` kiểm tra gom nhóm và xuất Excel bằng dữ liệu giả, không cần Revit.
Chạy `build.ps1 -RunSmokeTests` để build và chạy. Kết quả/fixture nằm trong `obj\SmokeTests`.
Kiểm tra tự động không thay thế manual test trong Revit; chưa xác nhận runtime trên model thực tế.

Manual test cho các tính năng Studio mới: Preview một view, đổi tick category và Refresh; thử link đã xoay và link unload; dùng Measurement rules cho Pipes/Walls/Floors rồi đối chiếu Properties; Save/Load preset và snapshot; so sánh Added/Removed/Changed; map cột không liên tiếp và kiểm tra công thức ở cột không map; chọn nhiều schedule có sẵn để xuất. Với Match BOM scope, kiểm tra element IDs/Count so với BOM host, xác nhận Preview không lưu parameter, Create tạo parameter chỉ đọc + schedule và Undo khôi phục cả batch. Thử worksharing/group để xác nhận không có batch lưu một phần khi marker không ghi được. Highlight thử cả host và linked instance.

## Các file chính

- `Class1.cs`: command dùng `[Transaction(TransactionMode.Manual)]`.
- `UI/ExportBomWindow.xaml` / `UI/ExportBomWindow.cs`: giao diện WPF modal, tông kem/nâu olive theo mẫu FamilyWizardWindow, chạy trong context của external command.
- `BomService.cs`: collector và đọc thông số Revit.
- `SpatialFilter.cs`: lọc Scope Box, Section Box và Crop Region.
- `BomAggregator.cs` / `Models.cs`: dữ liệu và gom nhóm.
- `BomColumns.cs`: cấu hình title/field, các cột bắt buộc và kiểm tra tên schedule.
- `UI/ConfigurationDialogs.cs`: cửa sổ đặt tên nhiều schedule và Modify Title/Fields.
- `ExcelExporter.cs`: tạo workbook hoặc điền template bằng Open XML.

## Giao diện và symbol

Symbol tại `Assets/bom-export-symbol.png`, được tạo bằng imagegen built-in theo phong cách bộ icon MEP bạn cung cấp: khung view với pipe/duct → bảng BOM. Prompt lưu tại `Assets/bom-export-symbol.prompt.txt`.
Icon hiển thị ở thanh tiêu đề, biểu tượng cửa sổ và vùng preview trống. XAML và PNG được nhúng vào DLL; Add-In Manager chỉ cần load DLL, không cần copy asset rời.
Style tham khảo: nền `#F5F2EE`, card trắng bo góc 16, chữ `#37322B`, accent `#90805C`, nút `#62543C`.

`Tests/UiPreview.csproj` load trực tiếp các resource từ DLL, kiểm tra XAML/binding và render ba trạng thái giao diện bằng dữ liệu giả vào `obj/UiPreview/Output`. Đây là kiểm tra WPF ngoài Revit, không xác nhận số category của model thực tế.

Manual test bổ sung: khi mới mở, scope/view chưa chọn, category/BOM trống. Chọn Entire Model → 3D → Floor Plan → Scope Box; mỗi lần thay phạm vi/view, dữ liệu cũ được xóa. Chỉ sau Preview BOM mới hiện category có element. Thử search, MEP/All/None và reload DLL mới qua Add-In Manager Manual.

## Tạo Schedule thật trong Revit

- Sau Preview, chọn một hoặc nhiều category. Nút **Create Revit Schedules** bật nếu có dữ liệu BOM và ít nhất một category hỗ trợ regular schedule.
- Bấm nút để hiện bảng **Category / Schedule name / Status**. Nhập tên cho từng category; tên mặc định `BOM - Pipes - Entire Model`, tự đề xuất hậu tố khi trùng. Kiểm tra tên trống, ký tự không hợp lệ hoặc trùng giữa các dòng/với view đã có. Category không hỗ trợ schedule được ghi rõ và bỏ qua.
- Bấm **Create schedules** để đóng cửa sổ tool và tạo toàn bộ batch. Mở schedule đầu tiên; các schedule còn lại nằm trong Project Browser. Dùng TransactionGroup: nếu một schedule lỗi thì rollback cả batch; thao tác batch có thể Undo cùng lúc. Cancel dialog không tạo schedule, giữ cửa sổ tool.
- Schedule thật thống kê category theo **toàn model**, áp dụng phase/thiết lập của Revit Schedule; không dùng tập Element IDs từ view/Scope Box và không đảm bảo tổng trùng BOM theo vùng.
- Thêm các field Family/Type, System, Size/dimension, Length và Count tùy khả năng schedule của category; Length dùng m, dimension dùng mm. Itemize every instance bật; có grand total.
- Không tạo parameter để đánh dấu element. Có thể Undo thao tác tạo schedule hoặc chỉnh field/filter trong Schedule Properties của Revit.
- Cần manual test trên Revit 2023: chọn Pipes + Ducts + Cable Trays, Preview, đặt ba tên, tạo batch và kiểm tra Project Browser/field/unit/count; thử tên trùng/Cancel, category không hỗ trợ schedule và Undo.

## Modify Title / Fields

- Bấm **Modify Title** sau Preview. Đổi title của BOM, tên heading, thứ tự cột bằng Move up/down; ẩn hoặc Remove cột phụ.
- Các cột chung bắt buộc **Category, Family, Type, Unit, Quantity, Element Count** không thể Remove. Category/Family/Type/Element Count luôn hiển thị; Unit và Quantity có thể ẩn/hiện, vẫn giữ dữ liệu và quy tắc đo.
- Danh sách **Available common parameters** gồm parameter instance/type có mặt trên mọi element đang được thống kê. Chọn parameter → Add field. Chưa chọn nhiều category vẫn phải thỏa điều kiện trên mọi element của category đó. Parameter có nhưng chưa gán giá trị sẽ để trống.
- Parameter thêm được hiển thị theo giá trị/đơn vị Revit. Nếu một nhóm BOM có nhiều giá trị khác nhau, hiển thị `<varies>`; không lấy ngẫu nhiên giá trị của element đầu tiên.
- Apply cập nhật preview, sheet BOM và sheet từng category; Cancel giữ cấu hình cũ. Các sheet Elements/Notes giữ cấu trúc audit.
- Khi thay phạm vi/category, parameter phụ không còn chung sẽ bị bỏ khỏi lần xuất đó và ghi trong Notes. Dùng Tools → Save preset để lưu cấu hình ra file.
- **Revit Schedule vẫn dùng các field mặc định**; Modify Title chỉ áp dụng BOM/Excel theo yêu cầu.
- Manual test: đổi Quantity thành tên khác, chuyển vị trí, ẩn System/Size, thêm Comments; thử xóa/ẩn Category/Quantity và kiểm tra bị khóa; export kiểm tra title/heading/value tương ứng trên BOM và các category sheet.

## Bảng gọn và Element Properties

- Phiên mới mặc định ẩn **System, Size, Unit và Quantity** trong cấu hình BOM. Hiện STT/Category/Family/Type/Element Count và cột **Element IDs** dạng nút **View**. Dùng Modify BOM Title để bật lại cột ẩn; xuất BOM Excel theo các field đang hiển thị. Preset đã lưu vẫn giữ cấu hình field của preset.
- Nhấn **View** mở danh sách instance của dòng BOM, gồm Element ID, Source, Category, Type, Link instance ID và Unique ID. Ctrl/Shift để chọn nhiều dòng; **Copy selected IDs** hoặc **Copy all IDs** copy mỗi ID trên một dòng. ID số trùng giữa host/link vẫn được giữ; Source và Link instance ID giúp phân biệt instance.
- **Properties** hoặc double-click dòng ID mở thông số của instance đó. **View in Revit / Close** đóng cửa sổ danh sách và tool, rồi chọn/zoom các instance đã chọn trong Revit. Dòng tổng/header group không có nút View. Excel vẫn xuất danh sách ID thật, không xuất chữ View.
- Double-click **dòng dữ liệu trong BOM Preview** mở cửa sổ Element Properties. Không mở khi double-click header, scrollbar hoặc dòng tổng/header group.
- Dòng gom nhóm có nhiều instance: dropdown liệt kê source + ID + family/type để chọn đúng instance, gồm cả linked element khi có trong BOM.
- Properties có các nhóm Identity/BOM/Instance/Type, tab **All / Instance / Type**, tìm theo tên hoặc giá trị và Ctrl+C copy dòng. System/Size/Quantity/Unit bị ẩn vẫn xem được ở nhóm BOM; parameter instance/type đọc từ Revit theo element chọn.
- Cửa sổ này chỉ xem; Access “Edit in Revit” nghĩa parameter có thể sửa bên Revit, không phải ô editable trong tool. Không ghi parameter hay tạo transaction.
- Manual test: double-click dòng Pipes có nhiều ID, đổi element trong dropdown và so với Properties của Revit; thử linked instance, search “Diameter”, tab Type, copy value; thử double-click dòng tổng để xác nhận không lấy nhầm element.
- Manual test Element IDs: nhấn View ở dòng gom nhiều instance; chọn nhiều ID và copy vào text editor; thử Properties; View in Revit để kiểm tra selection/zoom của host và linked instance. Kiểm tra ID trùng giữa nguồn và nội dung cột Element IDs trong Excel.
