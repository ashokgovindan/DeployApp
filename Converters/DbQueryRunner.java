import java.io.*;
import java.nio.file.*;
import java.sql.*;
import java.time.LocalDateTime;
import java.time.format.DateTimeFormatter;
import java.util.*;
import java.util.concurrent.*;
import javax.xml.parsers.*;
import org.w3c.dom.*;

public class DbQueryRunner {

    private static final Object receivedLock = new Object();
    private static final Object pendingLock = new Object();
    private static final Object processedLock = new Object();
    private static final Object logLock = new Object();
    private static final ConcurrentHashMap<String, Boolean> headersWritten = new ConcurrentHashMap<>();
    private static final DateTimeFormatter formatter = DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss");

    public static void main(String[] args) {
        String configPath = "config.xml";
        if (args.length > 0)
            configPath = args[0];

        File configFile = new File(configPath);
        if (!configFile.exists()) {
            createSampleConfig(configPath);
            System.out.println("Created sample config at " + configPath
                    + ". Please update it with your configuration and run the app again.");
            return;
        }

        try {
            DocumentBuilderFactory factory = DocumentBuilderFactory.newInstance();
            DocumentBuilder builder = factory.newDocumentBuilder();
            Document doc = builder.parse(configFile);
            doc.getDocumentElement().normalize();

            Element root = doc.getDocumentElement();

            // Read OutputFolder
            String outputFolder = getElementText(root, "OutputFolder", ".");
            File outputDir = new File(outputFolder);
            if (!outputDir.exists())
                outputDir.mkdirs();

            String receivedCsv = Paths.get(outputFolder, "Received.csv").toString();
            String pendingCsv = Paths.get(outputFolder, "Pending.csv").toString();
            String processedCsv = Paths.get(outputFolder, "Processed.csv").toString();
            String logFile = Paths.get(outputFolder, "ExecutionLog.txt").toString();

            // Do not overwrite files across multiple runs. Instead, check if headers were
            // already written.
            if (new File(receivedCsv).exists() && new File(receivedCsv).length() > 0)
                headersWritten.put("Received", true);
            if (new File(pendingCsv).exists() && new File(pendingCsv).length() > 0)
                headersWritten.put("Pending", true);
            if (new File(processedCsv).exists() && new File(processedCsv).length() > 0)
                headersWritten.put("Processed", true);

            // Read all RPA elements
            NodeList rpaNodes = root.getElementsByTagName("RPA");
            List<Element> rpas = new ArrayList<>();
            for (int i = 0; i < rpaNodes.getLength(); i++) {
                rpas.add((Element) rpaNodes.item(i));
            }

            // UCanAccess is a pure Java JDBC driver — fully thread-safe, supports true
            // multi-threading
            ExecutorService executor = Executors
                    .newFixedThreadPool(Math.min(rpas.size(), Runtime.getRuntime().availableProcessors()));
            List<Future<?>> futures = new ArrayList<>();

            for (Element rpa : rpas) {
                futures.add(executor.submit(() -> {
                    String rpaName = rpa.getAttribute("Name");
                    if (rpaName == null || rpaName.isEmpty())
                        rpaName = "Unknown RPA";

                    String db = getElementText(rpa, "Database", null);

                    Element queriesEl = getChildElement(rpa, "Queries");
                    String receivedQuery = queriesEl != null ? getElementText(queriesEl, "Received", null) : null;
                    String pendingQuery = queriesEl != null ? getElementText(queriesEl, "Pending", null) : null;
                    String processedQuery = queriesEl != null ? getElementText(queriesEl, "Processed", null) : null;

                    if (db == null || db.trim().isEmpty()) {
                        System.out.println("Skipping RPA '" + rpaName + "' because Database path is missing.");
                        return;
                    }

                    System.out.println(
                            "Processing '" + rpaName + "' (" + db + ") on thread " + Thread.currentThread().getName());
                    logMessage(logFile,
                            "[" + LocalDateTime.now().format(formatter) + "] Started RPA '" + rpaName + "'");

                    try {
                        // UCanAccess JDBC connection string for Access databases
                        String connStr = "jdbc:ucanaccess://" + db + ";memory=false";
                        try (Connection conn = DriverManager.getConnection(connStr)) {

                            // Received
                            processQuery(conn, receivedQuery, receivedCsv, receivedLock, "Received", rpaName, logFile);

                            // Pending
                            processQuery(conn, pendingQuery, pendingCsv, pendingLock, "Pending", rpaName, logFile);

                            // Processed
                            processQuery(conn, processedQuery, processedCsv, processedLock, "Processed", rpaName,
                                    logFile);
                        }
                    } catch (Exception ex) {
                        System.out.println("Error processing RPA '" + rpaName + "': " + ex.getMessage());
                        logMessage(logFile, "[" + LocalDateTime.now().format(formatter) + "] Error in RPA '" + rpaName
                                + "': " + ex.getMessage());
                    } finally {
                        logMessage(logFile,
                                "[" + LocalDateTime.now().format(formatter) + "] Finished RPA '" + rpaName + "'");
                    }
                }));
            }

            // Wait for all threads to complete
            for (Future<?> f : futures) {
                try {
                    f.get();
                } catch (Exception e) {
                    System.out.println("Thread error: " + e.getMessage());
                }
            }

            executor.shutdown();
            System.out.println("All databases processed successfully.");

        } catch (Exception ex) {
            System.out.println("Fatal error: " + ex.getMessage());
            ex.printStackTrace();
        }
    }

    private static void processQuery(Connection conn, String query, String outputFile, Object fileLock,
            String queryType, String rpaName, String logFile) {
        if (query == null || query.trim().isEmpty())
            return;

        logMessage(logFile, "[" + LocalDateTime.now().format(formatter) + "] Started " + queryType + " query for RPA '"
                + rpaName + "'");

        try (Statement stmt = conn.createStatement();
                ResultSet rs = stmt.executeQuery(query)) {

            ResultSetMetaData meta = rs.getMetaData();
            int columnCount = meta.getColumnCount();
            StringBuilder sb = new StringBuilder();

            // Write CSV Header dynamically based on the first query result
            if (!headersWritten.containsKey(queryType) && columnCount > 0) {
                List<String> headers = new ArrayList<>();
                headers.add("RPA Name"); // Added column to distinguish source
                for (int i = 1; i <= columnCount; i++) {
                    headers.add(escapeCsv(meta.getColumnName(i)));
                }

                synchronized (fileLock) {
                    if (!headersWritten.containsKey(queryType)) {
                        appendToFile(outputFile, String.join(",", headers) + System.lineSeparator());
                        headersWritten.put(queryType, true);
                    }
                }
            }

            // Append data rows
            while (rs.next()) {
                List<String> row = new ArrayList<>();
                row.add(escapeCsv(rpaName));
                for (int i = 1; i <= columnCount; i++) {
                    String value = rs.getString(i);
                    row.add(escapeCsv(value != null ? value : ""));
                }
                sb.append(String.join(",", row)).append(System.lineSeparator());
            }

            // Thread-safely flush to the CSV file
            if (sb.length() > 0) {
                synchronized (fileLock) {
                    appendToFile(outputFile, sb.toString());
                }
            }

        } catch (Exception ex) {
            System.out.println("Error executing " + queryType + " query for RPA '" + rpaName + "': " + ex.getMessage());
            logMessage(logFile, "[" + LocalDateTime.now().format(formatter) + "] Error in " + queryType
                    + " query for RPA '" + rpaName + "': " + ex.getMessage());
        } finally {
            logMessage(logFile, "[" + LocalDateTime.now().format(formatter) + "] Finished " + queryType
                    + " query for RPA '" + rpaName + "'");
        }
    }

    private static void logMessage(String logFile, String message) {
        synchronized (logLock) {
            appendToFile(logFile, message + System.lineSeparator());
        }
    }

    private static void appendToFile(String filePath, String content) {
        try (FileWriter fw = new FileWriter(filePath, true)) {
            fw.write(content);
        } catch (IOException ex) {
            System.out.println("Error writing to file " + filePath + ": " + ex.getMessage());
        }
    }

    private static String escapeCsv(String field) {
        if (field.contains(",") || field.contains("\"") || field.contains("\n") || field.contains("\r")) {
            return "\"" + field.replace("\"", "\"\"") + "\"";
        }
        return field;
    }

    // XML helper methods
    private static String getElementText(Element parent, String tagName, String defaultValue) {
        NodeList nodes = parent.getElementsByTagName(tagName);
        if (nodes.getLength() > 0) {
            // Get direct child only
            for (int i = 0; i < nodes.getLength(); i++) {
                if (nodes.item(i).getParentNode().equals(parent)) {
                    return nodes.item(i).getTextContent();
                }
            }
        }
        return defaultValue;
    }

    private static Element getChildElement(Element parent, String tagName) {
        NodeList nodes = parent.getElementsByTagName(tagName);
        for (int i = 0; i < nodes.getLength(); i++) {
            if (nodes.item(i).getParentNode().equals(parent)) {
                return (Element) nodes.item(i);
            }
        }
        return null;
    }

    private static void createSampleConfig(String path) {
        String xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<Config>\n" +
                "  <OutputFolder>Output</OutputFolder>\n" +
                "  <RPA Name=\"Project 1\">\n" +
                "    <Database>C:\\path\\to\\db1.accdb</Database>\n" +
                "    <Queries>\n" +
                "      <Received>SELECT * FROM ReceivedTable</Received>\n" +
                "      <Pending>SELECT * FROM PendingTable</Pending>\n" +
                "      <Processed>SELECT * FROM ProcessedTable</Processed>\n" +
                "    </Queries>\n" +
                "  </RPA>\n" +
                "  <RPA Name=\"Project 2\">\n" +
                "    <Database>C:\\path\\to\\db2.accdb</Database>\n" +
                "    <Queries>\n" +
                "      <Received>SELECT * FROM SomeOtherReceivedTable</Received>\n" +
                "      <Pending>SELECT * FROM SomeOtherPendingTable</Pending>\n" +
                "      <Processed>SELECT * FROM SomeOtherProcessedTable</Processed>\n" +
                "    </Queries>\n" +
                "  </RPA>\n" +
                "</Config>";
        try (FileWriter fw = new FileWriter(path)) {
            fw.write(xml);
        } catch (IOException ex) {
            System.out.println("Error creating sample config: " + ex.getMessage());
        }
    }
}
