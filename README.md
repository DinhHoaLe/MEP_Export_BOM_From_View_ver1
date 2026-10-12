# MEP Export BOM — Revit 2023

Add-in C# cho **Revit 2023**, `.NET Framework 4.8`, x64. Chạy bằng **Add-In Manager (Manual)**.
Command: `MEP_Export_BOM_From_View_ver1.ExportBomCommand`.
Preview/Excel chỉ đọc model. Nút Create Revit Schedule tạo một ViewSchedule mới bằng transaction khi người dùng bấm; không gán/sửa parameter của element.

## Chạy test bằng Add-In Manager

1. Mở project model trong Revit 2023.
2. Vào **Add-Ins → Add-In Manager (Manual)**. Tên/vị trí menu có thể khác tùy bản Add-In Manager bạn cài.
3. Chọn **Load** và mở `bin\Debug\MEP_Export_BOM_From_View_ver1.dll` trong thư mục project này.
4. Chọn `MEP_Export_BOM_From_View_ver1.ExportBomCommand` → **Run**.
5. Chọn phạm vi: **Entire Model / 3D View / Floor Plan / Scope Box**.
6. Chọn view hoặc Scope Box. Entire Model không cần đối tượng này.
7. Khi mới mở hoặc đổi phạm vi/view, danh sách category và bảng BOM trống. Bấm **Preview BOM** để quét phạm vi và hiện category có element; lần đầu chọn nhóm MEP mặc định, nếu không có MEP thì chọn toàn bộ nhóm có trong vùng.
8. Chọn lại category rồi bấm **Preview BOM**. **MEP / All / None** chọn/bỏ các nhóm. Review và Show all đã bỏ. Kiểm tra **Notes**; dùng **Modify Title** để đổi title/cột nếu cần.
9. Để trống Template để xuất form mặc định; hoặc chọn template `.xlsx`, tên sheet và dòng bắt đầu ghi.
10. Bấm **Export Excel**, chọn file mới và mở bằng Excel để kiểm tra.

Sau khi thay code, build lại và load/reload DLL trong Add-In Manager. Nếu DLL bị khóa, đóng cửa sổ tool; có thể cần khởi động lại Revit. Không cần cài manifest `.addin` riêng để chạy command qua Add-In Manager đã cài.

## Quy tắc thống kê

| Phạm vi | Cách lấy dữ liệu |
|---|---|
| Entire Model | Instance trong document hiện tại, theo category được chọn |
| 3D View | Collector theo view; lọc thêm bounding box nếu Section Box đang bật |
| Floor Plan | Collector theo view; kiểm tra bounding box với Crop Region nếu đang bật |
| Scope Box | Kiểm tra giao giữa bounding box element và vùng Scope Box, hỗ trợ Scope Box xoay |

- Bản này chỉ thống kê **host model**, chưa lấy element trong Revit Link.
- Chọn một view hoặc một Scope Box cho mỗi lần xuất.
- Collector theo view phản ánh thiết lập Revit, không phải kiểm tra từng pixel thấy trên màn hình. Đối tượng bị che bởi đối tượng khác vẫn có thể được lấy.
- Lọc vùng dựa trên bounding box, có thể lấy dư nếu box chứa khoảng trống. Element không có bounding box sẽ không được lấy khi cần lọc vùng.
- **Element giao biên được lấy toàn bộ khối lượng.** Ống dài 10 m, chỉ 3 m nằm trong Scope Box, vẫn thống kê 10 m.
- 3D perspective và floor plan có split crop chưa hỗ trợ. Dùng 3D orthographic và crop không split.
- `MEPCurve` (Pipe, Flex Pipe, Duct, Flex Duct, Cable Tray, Conduit...) cộng chiều dài, đơn vị `m`.
- Các category còn lại đếm instance, đơn vị `pcs`. Chọn Walls/Floors sẽ đếm số element, chưa tính diện tích/thể tích.
- Gom nhóm theo category ID, type ID, family/type, system, kích thước chính xác và đơn vị. Làm tròn hiển thị không làm gộp kích thước khác nhau.
- Kích thước lấy từ built-in parameters; fitting/equipment có thể lấy từ connector. Kích thước hiển thị bằng mm.
- System lấy từ parameter hoặc connector. Instance không có thông tin system để trống.
- Element đọc lỗi được ghi trong Notes; cần kiểm tra Notes trước khi dùng BOM.

## Form Excel mặc định

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
- Các cột chung bắt buộc **Category, Family, Type, Unit, Quantity, Element Count** có checkbox bị khóa và không thể Remove. Có thể đổi tên/thứ tự các cột này.
- Danh sách **Available common parameters** gồm parameter instance/type có mặt trên mọi element đang được thống kê. Chọn parameter → Add field. Chưa chọn nhiều category vẫn phải thỏa điều kiện trên mọi element của category đó. Parameter có nhưng chưa gán giá trị sẽ để trống.
- Parameter thêm được hiển thị theo giá trị/đơn vị Revit. Nếu một nhóm BOM có nhiều giá trị khác nhau, hiển thị `<varies>`; không lấy ngẫu nhiên giá trị của element đầu tiên.
- Apply cập nhật preview, sheet BOM và sheet từng category; Cancel giữ cấu hình cũ. Các sheet Elements/Notes giữ cấu trúc audit.
- Khi thay phạm vi/category, parameter phụ không còn chung sẽ bị bỏ khỏi lần xuất đó và ghi trong Notes. Các thiết lập cột giữ trong phiên cửa sổ tool, chưa lưu profile ra file.
- **Revit Schedule vẫn dùng các field mặc định**; Modify Title chỉ áp dụng BOM/Excel theo yêu cầu.
- Manual test: đổi Quantity thành tên khác, chuyển vị trí, ẩn System/Size, thêm Comments; thử xóa/ẩn Category/Quantity và kiểm tra bị khóa; export kiểm tra title/heading/value tương ứng trên BOM và các category sheet.
