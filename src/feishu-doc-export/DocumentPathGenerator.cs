using feishu_doc_export.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using feishu_doc_export.Helper;

namespace feishu_doc_export
{
    public static class DocumentPathGenerator
    {
        /// <summary>
        /// 文档objToken和路径的映射
        /// </summary>
        private static Dictionary<string, string> documentPaths;
        /// <summary>
        /// 文档nodeToken和路径的映射
        /// </summary>
        private static Dictionary<string, string> documentPaths2;

        public static void GenerateDocumentPaths(List<WikiNodeItemDto> documents, string rootFolderPath)
        {
            documentPaths = new Dictionary<string, string>();
            documentPaths2 = new Dictionary<string, string>();

            var topDocument = documents.Where(x => string.IsNullOrWhiteSpace(x.ParentNodeToken));
            foreach (var document in topDocument)
            {
                GenerateDocumentPath(document, rootFolderPath, documents);
            }

        }

        private static void GenerateDocumentPath(WikiNodeItemDto document, string parentFolderPath, List<WikiNodeItemDto> documents)
        {
            // 非法字符替换 + 超长截断（前60 + ...）—— 文件夹与文件名同规则，从根上杜绝 PathTooLongException
            string title = FileHelper.SafeName(document.Title);
            string documentFolderPath = Path.Combine(parentFolderPath, title);

            documentPaths[document.ObjToken] = documentFolderPath;
            documentPaths2[document.NodeToken] = documentFolderPath;

            foreach (var childDocument in GetChildDocuments(document, documents))
            {
                GenerateDocumentPath(childDocument, documentFolderPath, documents);
            }
        }

        private static IEnumerable<WikiNodeItemDto> GetChildDocuments(WikiNodeItemDto document, List<WikiNodeItemDto> documents)
        {
            return documents.Where(d => d.ParentNodeToken == document.NodeToken);
        }

        /// <summary>
        /// 获取文档的存储路径
        /// </summary>
        /// <param name="objToken"></param>
        /// <returns></returns>
        public static string GetDocumentPath(string objToken)
        {
            documentPaths.TryGetValue(objToken, out string path);
            return path;
        }

        /// <summary>
        /// 获取文档的存储路径
        /// </summary>
        /// <param name="objToken"></param>
        /// <returns></returns>
        public static string GetDocumentPathByNodeToken(string nodeToken)
        {
            documentPaths2.TryGetValue(nodeToken, out string path);
            return path;
        }
    }
}
